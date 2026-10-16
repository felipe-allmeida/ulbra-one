using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

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
public sealed class RealtimeConnectionRegistry(ILogger<RealtimeConnectionRegistry> logger)
{
    private sealed record Connection(
        RealtimeSubscription Subscription,
        Guid UserId,
        string? Role,
        bool IsAdmin,
        ConcurrentDictionary<(string Scope, Guid ResourceId), byte> Presence,
        // Último seq aplicado por (escopo, recurso) — sobrevive à remoção da entrada em Presence
        // (leave apaga de Presence, mas não daqui), senão um enter atrasado com seq menor que um leave
        // já aplicado reapareceria como presente. Vive no próprio Connection para ser descartado junto
        // quando a conexão cai — sem isso precisaria de limpeza manual em Remove().
        ConcurrentDictionary<(string Scope, Guid ResourceId), long> LastAppliedSeq);

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
        _connections[connectionId] = new Connection(subscription, userId, role, isAdmin, new(), new());
        return subscription;
    }

    public void Dispatch(RealtimeEnvelope envelope)
    {
        // Comando de presença dirigido a uma conexão: nunca vira um Signal no canal SSE — só muta o
        // registry local, e só quando esta réplica é a dona da conexão (ver ForConnection).
        if (envelope.Target.ConnectionId is { } connectionId)
        {
            ApplyPresenceCommand(connectionId, envelope.Target.UserId, envelope.Target.PresenceScope,
                envelope.Target.PresenceResourceId, envelope.Target.Seq, envelope.Signal.Kind);
            return;
        }

        foreach (var connection in _connections.Values)
            if (Matches(connection, envelope.Target))
                connection.Subscription.TryWrite(envelope.Signal);
    }

    /// <summary>Aplica um comando ForConnection recebido do backplane. O caso comum de descarte — a
    /// conexão não é desta réplica, porque o comando chegou em TODAS via fan-out e só uma é a dona — é
    /// esperado a cada enter/leave em produção, então fica em Debug para não virar ruído. O UserId de
    /// quem pediu não bater com o dono real da conexão é diferente: um connectionId é um GUID v4
    /// imprevisível entregue só ao próprio dono via SSE, então essa recusa é a branch de segurança —
    /// sem log, uma tentativa real de spoofing fica indistinguível de ruído normal de fan-out. Um envelope
    /// sem scope/resourceId/seq nunca deveria existir (só ForConnection monta esse alvo, e sempre
    /// preenche os três) — Warning porque isso só acontece por bug de quem publica.</summary>
    private void ApplyPresenceCommand(
        Guid connectionId, Guid? callerUserId, string? scope, Guid? resourceId, long? seq, string kind)
    {
        if (scope is null || resourceId is null || seq is null)
        {
            logger.LogWarning(
                "Comando de presença ForConnection malformado (scope/resourceId/seq nulo) para conexão "
                + "{ConnectionId}, ignorado.", connectionId);
            return;
        }

        if (!_connections.TryGetValue(connectionId, out var connection))
        {
            logger.LogDebug(
                "Comando de presença {Kind} para conexão {ConnectionId} ignorado: não é desta réplica.",
                kind, connectionId);
            return;
        }

        if (connection.UserId != callerUserId)
        {
            logger.LogWarning(
                "Comando de presença {Kind} para conexão {ConnectionId} recusado: chamador {CallerUserId} "
                + "não é o dono (dono real: {OwnerUserId}).",
                kind, connectionId, callerUserId, connection.UserId);
            return;
        }

        // Corrida entre publishes independentes (enter/leave sem afinidade de réplica): o seq do
        // cliente — a única fonte de ordem verdadeira, já que réplicas não compartilham relógio —
        // decide. Um comando com seq ≤ o último aplicado para esta (conexão, escopo, recurso) chegou
        // atrasado e é descartado, mesmo que seu Kind já não bata com o que já foi aplicado. Com um
        // cliente correto isso nunca deveria acontecer — quando acontece é o diagnóstico que explicaria
        // uma presença fantasma em produção, daí o Warning (ver TryAdvanceSeq).
        if (!TryAdvanceSeq(connection.LastAppliedSeq, (scope, resourceId.Value), seq.Value, connectionId, kind)) return;

        switch (kind)
        {
            case PresenceCommandKind.Enter:
                EnterPresence(connectionId, scope, resourceId.Value);
                break;
            case PresenceCommandKind.Leave:
                LeavePresence(connectionId, scope, resourceId.Value);
                break;
            default:
                // Kind desconhecido chegando num alvo ForConnection é sempre uma anomalia (skew de
                // versão entre réplicas, ou um bug) — nunca deveria acontecer em operação normal.
                logger.LogWarning(
                    "Comando de presença com Kind desconhecido {Kind} para conexão {ConnectionId} ignorado.",
                    kind, connectionId);
                break;
        }
    }

    /// <summary>CAS: só avança (e retorna true) se <paramref name="seq"/> for estritamente maior que o
    /// último valor guardado para a chave — atômico mesmo com comandos concorrentes para a mesma
    /// (conexão, escopo, recurso). Loga a rejeição: é a branch que faz uma presença ficar presa (um
    /// leave descartado nunca aparece de novo sozinho), então silenciá-la esconderia exatamente o
    /// diagnóstico que explicaria o sintoma em produção.</summary>
    private bool TryAdvanceSeq(
        ConcurrentDictionary<(string Scope, Guid ResourceId), long> lastAppliedSeq,
        (string Scope, Guid ResourceId) key, long seq, Guid connectionId, string kind)
    {
        while (true)
        {
            if (lastAppliedSeq.TryGetValue(key, out var current))
            {
                if (seq <= current)
                {
                    logger.LogWarning(
                        "Comando de presença {Kind} para conexão {ConnectionId} ({Scope}/{ResourceId}) "
                        + "descartado: seq {Seq} <= último aplicado {LastAppliedSeq}.",
                        kind, connectionId, key.Scope, key.ResourceId, seq, current);
                    return false;
                }
                if (lastAppliedSeq.TryUpdate(key, seq, current)) return true;
            }
            else if (lastAppliedSeq.TryAdd(key, seq))
            {
                return true;
            }
        }
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

    /// <summary>Espelha NotificationAudience.For (NotificationAudience.cs — a definição da predicate, não um
    /// dos call sites dela) para notificação, e casa
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
