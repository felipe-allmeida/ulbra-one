using MassTransit;

namespace Ulbra.One.Finance.Infrastructure.Messaging;

public sealed class PayableReceivedConsumerDefinition : ConsumerDefinition<PayableReceivedConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<PayableReceivedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        // Immediate in-process retry for GENUINE transient faults only (e.g. an xmin concurrency race with a
        // human edit) — no delayed redelivery, so nothing here depends on a broker's delayed-message support.
        // The consumer settles unclassifiable payables itself (never throws for "no suggestion"), so there is
        // no classification retry to schedule and a payable can never get stuck in Received.
        endpointConfigurator.UseMessageRetry(r => r.Immediate(3));
    }
}
