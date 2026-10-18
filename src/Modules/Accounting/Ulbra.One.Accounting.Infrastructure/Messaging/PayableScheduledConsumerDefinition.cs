using MassTransit;

namespace Ulbra.One.Accounting.Infrastructure.Messaging;

/// <summary>Interval (not immediate) retry: under a concurrent redelivery the loser must wait for the winner's
/// commit so AlreadyPostedAsync/AlreadyPendingAsync sees the row (the unique indexes on journal-entry/pending
/// source-key make the retry a no-op), and the spacing avoids re-colliding on the max+1 (CompanyId, Number)
/// numbering. Without this the losing message faults straight to the _error queue.</summary>
public sealed class PayableScheduledConsumerDefinition : ConsumerDefinition<PayableScheduledConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<PayableScheduledConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(r => r.Intervals(
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));
    }
}
