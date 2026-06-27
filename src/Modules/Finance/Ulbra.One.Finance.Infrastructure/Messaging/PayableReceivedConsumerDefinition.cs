using MassTransit;

namespace Ulbra.One.Finance.Infrastructure.Messaging;

public sealed class PayableReceivedConsumerDefinition : ConsumerDefinition<PayableReceivedConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<PayableReceivedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseDelayedRedelivery(r => r.Intervals(ClassificationRetry.Intervals));
    }
}
