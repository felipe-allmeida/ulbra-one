using Microsoft.Extensions.Logging.Abstractions;
using Ulbra.One.Common.Application.Realtime;
using Ulbra.One.Common.Infrastructure.Realtime;
using Xunit;

namespace Ulbra.One.UnitTests.Realtime;

/// <summary>§7.2 do design: no drain a réplica "também remove as entradas de presença das suas conexões —
/// deploy não deixa avatar fantasma por 45 s". Isso nunca tinha sido construído: StopAsync só chamava
/// CompleteAll(), e LeavePresenceAsync tinha um único chamador, o endpoint HTTP. Sem estes testes, um rolling
/// deploy deixava todo mundo hospedado na réplica derrubada aparecendo como presente até o TTL vencer.</summary>
public class RealtimeDrainTests
{
    private static (RealtimeBackplaneListener Listener, InMemoryRealtimeBackplane Backplane, RealtimeConnectionRegistry Registry)
        Build()
    {
        var backplane = new InMemoryRealtimeBackplane(TimeProvider.System);
        var registry = new RealtimeConnectionRegistry(NullLogger<RealtimeConnectionRegistry>.Instance);
        var listener = new RealtimeBackplaneListener(
            backplane, registry, NullLogger<RealtimeBackplaneListener>.Instance);
        return (listener, backplane, registry);
    }

    [Fact]
    public async Task Drain_remove_a_presenca_das_conexoes_desta_replica()
    {
        var (listener, backplane, registry) = Build();
        var userId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        using var sub = registry.Subscribe(userId, "Requester", isAdmin: false);
        registry.EnterPresence(sub.ConnectionId, "requisition", resourceId);
        await backplane.EnterPresenceAsync("requisition", resourceId, userId, CancellationToken.None);
        Assert.NotEmpty(await backplane.ReadPresenceAsync("requisition", resourceId, CancellationToken.None));

        await listener.StopAsync(CancellationToken.None);

        Assert.Empty(await backplane.ReadPresenceAsync("requisition", resourceId, CancellationToken.None));
    }

    [Fact]
    public async Task Drain_avisa_quem_esta_vendo_o_recurso_nas_outras_replicas()
    {
        // Sem o sinal, os avatares só sumiriam da tela alheia no próximo invalidate por acaso: o cliente que
        // continua conectado em OUTRA réplica não tem por que refazer o fetch da presença.
        var (listener, backplane, registry) = Build();
        var signals = new List<RealtimeEnvelope>();
        await backplane.SubscribeAsync(e => { signals.Add(e); return Task.CompletedTask; }, CancellationToken.None);

        var resourceId = Guid.NewGuid();
        using var sub = registry.Subscribe(Guid.NewGuid(), "Requester", isAdmin: false);
        registry.EnterPresence(sub.ConnectionId, "requisition", resourceId);

        await listener.StopAsync(CancellationToken.None);

        Assert.Contains(signals, e =>
            e.Signal.Kind == "presence" && e.Target.PresenceResourceId == resourceId);
    }

    [Fact]
    public async Task Drain_continua_encerrando_os_canais()
    {
        // A limpeza de presença não pode ter custado o comportamento que já existia: os loops SSE precisam
        // terminar pelo canal completado, em vez de morrerem no meio do drain.
        var (listener, _, registry) = Build();
        using var sub = registry.Subscribe(Guid.NewGuid(), "Requester", isAdmin: false);

        await listener.StopAsync(CancellationToken.None);

        Assert.True(sub.Reader.Completion.IsCompleted);
    }
}
