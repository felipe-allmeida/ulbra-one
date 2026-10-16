using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Ulbra.One.Common.Application.Realtime;

/// <summary>Uma conexão SSE viva. Descartar remove a conexão e a presença dela.</summary>
public sealed class RealtimeSubscription(Guid connectionId, Channel<RealtimeSignal> channel, Action<Guid> onDispose)
    : IDisposable
{
    public Guid ConnectionId { get; } = connectionId;
    public ChannelReader<RealtimeSignal> Reader => channel.Reader;

    internal bool TryWrite(RealtimeSignal signal) => channel.Writer.TryWrite(signal);
    internal void Complete() => channel.Writer.TryComplete();

    public void Dispose() => onDispose(ConnectionId);
}

/// <summary>As conexões SSE vivas DESTA réplica. O estado compartilhado entre réplicas mora no
/// backplane; aqui fica só o que é local — os canais, e em qual recurso cada conexão está presente
/// (necessário para rotear sinal de presença e para renovar o TTL no heartbeat).</summary>
public sealed class RealtimeConnectionRegistry
{
    private sealed record Connection(
        RealtimeSubscription Subscription,
        Guid UserId,
        string? Role,
        bool IsAdmin,
        ConcurrentDictionary<(string Scope, Guid ResourceId), byte> Presence);

    private readonly ConcurrentDictionary<Guid, Connection> _connections = new();

    public RealtimeSubscription Subscribe(Guid userId, string? role, bool isAdmin)
    {
        var connectionId = Guid.NewGuid();
        // DropOldest: um cliente lento não pode acumular memória, e sinal perdido custa pouco —
        // todo sinal significa "invalide e refaça o fetch", então dois coalescidos = um (§3.3).
        var channel = Channel.CreateBounded<RealtimeSignal>(new BoundedChannelOptions(RealtimeDefaults.ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

        var subscription = new RealtimeSubscription(connectionId, channel, Remove);
        _connections[connectionId] = new Connection(subscription, userId, role, isAdmin, new());
        return subscription;
    }

    public void Dispatch(RealtimeEnvelope envelope)
    {
        // Comando de presença dirigido a uma conexão: nunca vira um Signal no canal SSE — só muta o
        // registry local, e só quando esta réplica é a dona da conexão (ver ForConnection).
        if (envelope.Target.ConnectionId is { } connectionId)
        {
            ApplyPresenceCommand(connectionId, envelope.Target.UserId, envelope.Target.PresenceScope,
                envelope.Target.PresenceResourceId, envelope.Signal.Kind);
            return;
        }

        foreach (var connection in _connections.Values)
            if (Matches(connection, envelope.Target))
                connection.Subscription.TryWrite(envelope.Signal);
    }

    /// <summary>Aplica um comando ForConnection recebido do backplane. Descarta em silêncio quando: a
    /// conexão não é desta réplica (o caso comum — o comando chegou em TODAS as réplicas via fan-out,
    /// só uma é a dona); ou o UserId de quem pediu não bate com o dono real da conexão — um
    /// connectionId é um GUID v4 imprevisível entregue só ao próprio dono via SSE, mas nada impede um
    /// caller malicioso de tentar usar o de outro usuário, e só a réplica dona sabe de quem é essa
    /// conexão para recusar.</summary>
    private void ApplyPresenceCommand(Guid connectionId, Guid? callerUserId, string? scope, Guid? resourceId, string kind)
    {
        if (scope is null || resourceId is null) return;
        if (!_connections.TryGetValue(connectionId, out var connection) || connection.UserId != callerUserId) return;

        if (kind == PresenceCommandKind.Enter) EnterPresence(connectionId, scope, resourceId.Value);
        else if (kind == PresenceCommandKind.Leave) LeavePresence(connectionId, scope, resourceId.Value);
    }

    public void EnterPresence(Guid connectionId, string scope, Guid resourceId)
    {
        if (_connections.TryGetValue(connectionId, out var connection))
            connection.Presence[(scope, resourceId)] = 0;
    }

    public void LeavePresence(Guid connectionId, string scope, Guid resourceId)
    {
        if (_connections.TryGetValue(connectionId, out var connection))
            connection.Presence.TryRemove((scope, resourceId), out _);
    }

    /// <summary>Chaves de presença das conexões desta réplica — o que o heartbeat renova (§6.2).</summary>
    public IReadOnlyCollection<PresenceKey> LocalPresence() =>
        _connections.Values
            .SelectMany(c => c.Presence.Keys.Select(p => new PresenceKey(p.Scope, p.ResourceId, c.UserId)))
            .Distinct()
            .ToArray();

    /// <summary>Encerra todos os canais para que os loops SSE terminem, em vez de morrerem no meio do
    /// drain do Container Apps.</summary>
    public void CompleteAll()
    {
        foreach (var connection in _connections.Values) connection.Subscription.Complete();
    }

    private void Remove(Guid connectionId)
    {
        if (_connections.TryRemove(connectionId, out var connection)) connection.Subscription.Complete();
    }

    /// <summary>Espelha NotificationAudience.For (ListNotifications.cs:42) para notificação, e casa
    /// presença pelo recurso em que a conexão está. A guarda de role nula é mantida para que a forma
    /// desta predicate mirror exatamente a do EF, impedindo deriva entre as duas implementações.</summary>
    private static bool Matches(Connection connection, RealtimeTarget target)
    {
        if (target.PresenceScope is { } scope && target.PresenceResourceId is { } resourceId)
            return connection.Presence.ContainsKey((scope, resourceId));

        if (target.UserId is { } userId) return connection.UserId == userId;

        if (target.Role is { } role)
            return connection.IsAdmin || (connection.Role != null && connection.Role == role);

        return false;
    }
}
