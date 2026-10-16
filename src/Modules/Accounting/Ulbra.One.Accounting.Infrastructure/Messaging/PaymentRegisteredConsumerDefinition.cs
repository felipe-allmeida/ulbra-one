using MassTransit;

namespace Ulbra.One.Accounting.Infrastructure.Messaging;

/// <summary>Interval retry so a concurrent redelivery loser waits for the winner's commit before re-running into
/// the idempotency check (the unique source-key index makes the retry a no-op) and doesn't re-collide on the
/// max+1 (CompanyId, Number) numbering. Otherwise the loser faults straight to the _error queue.</summary>
public sealed class PaymentRegisteredConsumerDefinition : ConsumerDefinition<PaymentRegisteredConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<PaymentRegisteredConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(r => r.Intervals(
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));
    }
}
