using MassTransit;
using Ulbra.One.Api;
using Xunit;

namespace Ulbra.One.IntegrationTests.Notifications;

/// <summary>Guards against the silent-loss failure mode MessagingSetup.OutboxEnrolledEndpoints documents: a
/// consumer that calls IPublishEndpoint.Publish but is NOT enrolled in the EF consumer outbox stages its outbox
/// row on the DbContext, then loses it on scope dispose — no exception, no log, nothing a functional test would
/// catch. Reflects over every IConsumer&lt;T&gt; in the Notifications infrastructure assembly whose constructor
/// takes an IPublishEndpoint, so a sixth publishing consumer added later without enrolling it fails this test
/// instead of losing notifications in production. No database needed — this only inspects types and the
/// registration set built at class-load time.</summary>
public class NotificationConsumerOutboxEnrollmentTests
{
    [Fact]
    public void Todos_os_consumers_de_Notifications_que_publicam_estao_enrolados_no_outbox()
    {
        var publishingConsumers = typeof(Ulbra.One.Notifications.Infrastructure.Messaging.NotificationCreatedConsumer)
            .Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract
                && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsumer<>))
                && t.GetConstructors().Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(IPublishEndpoint))))
            .ToList();

        // Sanity check on the reflection query itself: today there are exactly five (the transition/request/
        // attached/breach consumers). NotificationCreatedConsumer doesn't count — it consumes NotificationCreated
        // and publishes to the realtime backplane, not via IPublishEndpoint. If this count ever changes, update
        // it deliberately — don't just bump the number to make the test pass.
        Assert.Equal(5, publishingConsumers.Count);

        var formatter = DefaultEndpointNameFormatter.Instance;
        var consumerMethod = typeof(IEndpointNameFormatter).GetMethod(nameof(IEndpointNameFormatter.Consumer))!;

        foreach (var consumerType in publishingConsumers)
        {
            var endpointName = (string)consumerMethod.MakeGenericMethod(consumerType).Invoke(formatter, null)!;
            Assert.True(MessagingSetup.OutboxEnrolledEndpoints.Contains(endpointName),
                $"{consumerType.Name} publishes via IPublishEndpoint but its endpoint '{endpointName}' is not in " +
                "MessagingSetup.OutboxEnrolledEndpoints — its publish would silently drop the message on scope dispose.");
        }
    }
}
