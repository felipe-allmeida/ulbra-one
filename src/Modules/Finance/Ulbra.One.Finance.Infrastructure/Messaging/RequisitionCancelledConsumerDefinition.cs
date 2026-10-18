using MassTransit;

namespace Ulbra.One.Finance.Infrastructure.Messaging;

public sealed class RequisitionCancelledConsumerDefinition : ConsumerDefinition<RequisitionCancelledConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<RequisitionCancelledConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        // Immediate in-process retry for transient faults (e.g. an xmin race with a concurrent payment/edit on
        // the same Payable). The status-based idempotency makes a retry a no-op once the cancel has committed.
        endpointConfigurator.UseMessageRetry(r => r.Immediate(3));
    }
}
