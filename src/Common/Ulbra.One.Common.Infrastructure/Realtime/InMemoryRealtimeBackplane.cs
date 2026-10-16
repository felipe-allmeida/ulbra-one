using System.Collections.Concurrent;
using Ulbra.One.Common.Application.Realtime;

namespace Ulbra.One.Common.Infrastructure.Realtime;

/// <summary>Backplane de processo único, para teste. NÃO é modo de desenvolvimento: o compose sobe
/// Redis de verdade, porque rodar local contra substrato diferente do de produção é a armadilha que já
/// existe com RabbitMQ/Service Bus e que não vamos duplicar (§7.1 do design).</summary>
public sealed class InMemoryRealtimeBackplane(TimeProvider clock) : IRealtimeBackplane
{
    private readonly ConcurrentBag<Func<RealtimeEnvelope, Task>> _handlers = [];
    private readonly ConcurrentDictionary<PresenceKey, DateTimeOffset> _presence = new();

    public async Task PublishAsync(RealtimeEnvelope envelope, CancellationToken ct)
    {
        foreach (var handler in _handlers) await handler(envelope);
    }

    public Task SubscribeAsync(Func<RealtimeEnvelope, Task> handler, CancellationToken ct)
    {
        _handlers.Add(handler);
        return Task.CompletedTask;
    }

    public Task EnterPresenceAsync(string scope, Guid resourceId, Guid userId, CancellationToken ct)
    {
        _presence[new PresenceKey(scope, resourceId, userId)] = Expiry;
        return Task.CompletedTask;
    }

    public Task RenewPresenceAsync(IReadOnlyCollection<PresenceKey> keys, CancellationToken ct)
    {
        foreach (var key in keys) _presence[key] = Expiry;
        return Task.CompletedTask;
    }

    public Task LeavePresenceAsync(string scope, Guid resourceId, Guid userId, CancellationToken ct)
    {
        _presence.TryRemove(new PresenceKey(scope, resourceId, userId), out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<Guid>> ReadPresenceAsync(string scope, Guid resourceId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        IReadOnlyCollection<Guid> present = _presence
            .Where(e => e.Key.Scope == scope && e.Key.ResourceId == resourceId && e.Value > now)
            .Select(e => e.Key.UserId)
            .ToArray();
        return Task.FromResult(present);
    }

    private DateTimeOffset Expiry => clock.GetUtcNow() + RealtimeDefaults.PresenceTtl;
}
