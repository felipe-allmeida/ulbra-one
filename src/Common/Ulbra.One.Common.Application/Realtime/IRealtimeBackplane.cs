namespace Ulbra.One.Common.Application.Realtime;

/// <summary>Espalhamento entre réplicas + estado de presença compartilhado. Implementado por Redis
/// (produção e dev) e por uma versão in-memory usada apenas em teste.</summary>
public interface IRealtimeBackplane
{
    Task PublishAsync(RealtimeEnvelope envelope, CancellationToken ct);

    /// <summary>Registra o handler chamado a cada envelope recebido. Chamado uma vez por réplica.</summary>
    Task SubscribeAsync(Func<RealtimeEnvelope, Task> handler, CancellationToken ct);

    Task EnterPresenceAsync(string scope, Guid resourceId, Guid userId, CancellationToken ct);

    /// <summary>Estende o TTL das chaves informadas. Chamado pelo heartbeat SSE.</summary>
    Task RenewPresenceAsync(IReadOnlyCollection<PresenceKey> keys, CancellationToken ct);

    Task LeavePresenceAsync(string scope, Guid resourceId, Guid userId, CancellationToken ct);

    Task<IReadOnlyCollection<Guid>> ReadPresenceAsync(string scope, Guid resourceId, CancellationToken ct);
}
