using Ulbra.One.Common.Application.Realtime;
using Ulbra.One.Common.Infrastructure.Realtime;
using Ulbra.One.Notifications.Application.Messaging;
using Ulbra.One.Notifications.Infrastructure.Messaging;
using Xunit;

namespace Ulbra.One.UnitTests.Notifications;

public class NotificationCreatedConsumerTests
{
    [Fact]
    public async Task Notificacao_de_role_publica_envelope_de_role()
    {
        var backplane = new InMemoryRealtimeBackplane(TimeProvider.System);
        var received = new List<RealtimeEnvelope>();
        await backplane.SubscribeAsync(e => { received.Add(e); return Task.CompletedTask; }, CancellationToken.None);
        var consumer = new NotificationCreatedConsumer(backplane);
        var notificationId = Guid.NewGuid();

        await consumer.ProcessAsync(
            new NotificationCreated(notificationId, AudienceUserId: null, AudienceRole: "Accounting"),
            CancellationToken.None);

        var envelope = Assert.Single(received);
        Assert.Equal(RealtimeTarget.ForRole("Accounting"), envelope.Target);
        Assert.Equal("notification", envelope.Signal.Kind);
        Assert.Equal(notificationId.ToString(), envelope.Signal.ResourceId);
    }

    [Fact]
    public async Task Notificacao_pessoal_publica_envelope_de_usuario()
    {
        var backplane = new InMemoryRealtimeBackplane(TimeProvider.System);
        var received = new List<RealtimeEnvelope>();
        await backplane.SubscribeAsync(e => { received.Add(e); return Task.CompletedTask; }, CancellationToken.None);
        var consumer = new NotificationCreatedConsumer(backplane);
        var user = Guid.NewGuid();
        var notificationId = Guid.NewGuid();

        await consumer.ProcessAsync(
            new NotificationCreated(notificationId, AudienceUserId: user, AudienceRole: null),
            CancellationToken.None);

        var envelope = Assert.Single(received);
        Assert.Equal(RealtimeTarget.ForUser(user), envelope.Target);
        Assert.Equal(notificationId.ToString(), envelope.Signal.ResourceId);
    }
}
