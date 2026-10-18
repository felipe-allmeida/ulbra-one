using Ulbra.One.Common.Application.Realtime;
using Ulbra.One.Common.Infrastructure.Realtime;

namespace Ulbra.One.UnitTests.Realtime;

public class InMemoryRealtimeBackplaneTests
{
    private static readonly TimeProvider Clock = TimeProvider.System;

    [Fact]
    public async Task Publish_entrega_ao_assinante()
    {
        var backplane = new InMemoryRealtimeBackplane(Clock);
        var received = new List<RealtimeEnvelope>();
        await backplane.SubscribeAsync(e => { received.Add(e); return Task.CompletedTask; }, CancellationToken.None);

        var envelope = new RealtimeEnvelope(
            new RealtimeTarget(UserId: null, Role: "Accounting", PresenceScope: null, PresenceResourceId: null),
            new RealtimeSignal("notification", null));
        await backplane.PublishAsync(envelope, CancellationToken.None);

        Assert.Equal(envelope, Assert.Single(received));
    }

    [Fact]
    public async Task Presenca_lista_quem_entrou()
    {
        var backplane = new InMemoryRealtimeBackplane(Clock);
        var resource = Guid.NewGuid();
        var user = Guid.NewGuid();

        await backplane.EnterPresenceAsync("requisition", resource, user, CancellationToken.None);

        Assert.Equal([user], await backplane.ReadPresenceAsync("requisition", resource, CancellationToken.None));
    }

    [Fact]
    public async Task Presenca_expira_sem_renovacao()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var backplane = new InMemoryRealtimeBackplane(clock);
        var resource = Guid.NewGuid();
        var user = Guid.NewGuid();
        await backplane.EnterPresenceAsync("requisition", resource, user, CancellationToken.None);

        clock.Advance(RealtimeDefaults.PresenceTtl + TimeSpan.FromSeconds(1));

        Assert.Empty(await backplane.ReadPresenceAsync("requisition", resource, CancellationToken.None));
    }

    [Fact]
    public async Task Renovacao_mantem_presenca_viva()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var backplane = new InMemoryRealtimeBackplane(clock);
        var resource = Guid.NewGuid();
        var user = Guid.NewGuid();
        await backplane.EnterPresenceAsync("requisition", resource, user, CancellationToken.None);

        clock.Advance(TimeSpan.FromSeconds(30));
        await backplane.RenewPresenceAsync([new PresenceKey("requisition", resource, user)], CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal([user], await backplane.ReadPresenceAsync("requisition", resource, CancellationToken.None));
    }

    [Fact]
    public async Task Sair_remove_antes_do_ttl()
    {
        var backplane = new InMemoryRealtimeBackplane(Clock);
        var resource = Guid.NewGuid();
        var user = Guid.NewGuid();
        await backplane.EnterPresenceAsync("requisition", resource, user, CancellationToken.None);

        await backplane.LeavePresenceAsync("requisition", resource, user, CancellationToken.None);

        Assert.Empty(await backplane.ReadPresenceAsync("requisition", resource, CancellationToken.None));
    }
}
