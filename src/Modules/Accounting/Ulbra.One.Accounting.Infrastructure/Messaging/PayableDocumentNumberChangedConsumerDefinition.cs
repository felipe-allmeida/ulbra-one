using MassTransit;

namespace Ulbra.One.Accounting.Infrastructure.Messaging;

/// <summary>Interval retry so a concurrent redelivery (e.g. an edit racing the payable's scheduling/settlement
/// posting) waits for the winner's commit before re-running — the amend is idempotent, so the retry then finds
/// the entries and applies the same value. Otherwise a transient race could fault straight to the _error queue.</summary>
public sealed class PayableDocumentNumberChangedConsumerDefinition : ConsumerDefinition<PayableDocumentNumberChangedConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<PayableDocumentNumberChangedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(r => r.Intervals(
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));
    }
}
