using MassTransit;

namespace Ulbra.One.Finance.Infrastructure.Messaging;

/// <summary>Short in-memory retry for transient failures — chiefly the xmin
/// DbUpdateConcurrencyException when a human edit races the recalculation. No delayed redelivery:
/// unlike classification there is no external dependency to wait out, and a later edit republishes.</summary>
public sealed class PayableUpdatedConsumerDefinition : ConsumerDefinition<PayableUpdatedConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<PayableUpdatedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(r => r.Intervals(
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));
    }
}
