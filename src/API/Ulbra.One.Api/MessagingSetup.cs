using Azure.Identity;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Persistence;

namespace Ulbra.One.Api;

internal static class MessagingSetup
{
    /// <summary>Endpoint names enrolled in the EF consumer outbox (built below). A consumer whose <c>Consume</c>
    /// calls <c>IPublishEndpoint.Publish</c> and is NOT listed here silently loses the message: the scoped
    /// endpoint stages the outbox row on the DbContext, no later <c>SaveChanges</c> ever commits it, and the row
    /// is discarded on scope dispose — no exception, no log. Internal (not private) so
    /// <c>NotificationConsumerOutboxEnrollmentTests</c> can assert every publishing Notifications consumer is
    /// present, catching a sixth one added without enrollment at test time instead of in production.</summary>
    internal static readonly HashSet<string> OutboxEnrolledEndpoints = new(StringComparer.OrdinalIgnoreCase)
    {
        DefaultEndpointNameFormatter.Instance.Consumer<Ulbra.One.Finance.Infrastructure.Messaging.PayableReceivedConsumer>(),
        DefaultEndpointNameFormatter.Instance.Consumer<Ulbra.One.Finance.Infrastructure.Messaging.RequisitionApprovedForPaymentConsumer>(),
        DefaultEndpointNameFormatter.Instance.Consumer<Ulbra.One.Finance.Infrastructure.Messaging.RequisitionCycleAddedForPaymentConsumer>(),
        DefaultEndpointNameFormatter.Instance.Consumer<Ulbra.One.Finance.Infrastructure.Messaging.RequisitionCancelledConsumer>(),
        DefaultEndpointNameFormatter.Instance.Consumer<Ulbra.One.Finance.Infrastructure.Messaging.RequisitionAttachmentAddedConsumer>(),
        DefaultEndpointNameFormatter.Instance.Consumer<Ulbra.One.Budgeting.Infrastructure.Messaging.PayableRejectedConsumer>(),
        // Realtime: each of these publishes NotificationCreated when it inserts a notification, atomically
        // with the SaveChanges that persists it.
        DefaultEndpointNameFormatter.Instance.Consumer<Ulbra.One.Notifications.Infrastructure.Messaging.RequisitionTransitionedConsumer>(),
        DefaultEndpointNameFormatter.Instance.Consumer<Ulbra.One.Notifications.Infrastructure.Messaging.BudgetAdjustmentTransitionedConsumer>(),
        DefaultEndpointNameFormatter.Instance.Consumer<Ulbra.One.Notifications.Infrastructure.Messaging.RequisitionFiscalDocumentRequestedConsumer>(),
        DefaultEndpointNameFormatter.Instance.Consumer<Ulbra.One.Notifications.Infrastructure.Messaging.RequisitionFiscalDocumentAttachedConsumer>(),
        DefaultEndpointNameFormatter.Instance.Consumer<Ulbra.One.Notifications.Infrastructure.Messaging.RequisitionSlaBreachedConsumer>(),
    };

    public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMassTransit(x =>
        {
            x.AddConsumer<Ulbra.One.Finance.Infrastructure.Messaging.PayableReceivedConsumer, Ulbra.One.Finance.Infrastructure.Messaging.PayableReceivedConsumerDefinition>();
            x.AddConsumer<Ulbra.One.Finance.Infrastructure.Messaging.PayableUpdatedConsumer, Ulbra.One.Finance.Infrastructure.Messaging.PayableUpdatedConsumerDefinition>();
            // Interval retry (AEL-856) so a concurrent redelivery loser waits out the winner's commit and no-ops
            // via the unique source-key indexes, instead of faulting straight to the _error queue.
            x.AddConsumer<Ulbra.One.Accounting.Infrastructure.Messaging.PayableScheduledConsumer, Ulbra.One.Accounting.Infrastructure.Messaging.PayableScheduledConsumerDefinition>();
            x.AddConsumer<Ulbra.One.Accounting.Infrastructure.Messaging.PaymentRegisteredConsumer, Ulbra.One.Accounting.Infrastructure.Messaging.PaymentRegisteredConsumerDefinition>();
            x.AddConsumer<Ulbra.One.Accounting.Infrastructure.Messaging.AccountingReversalConsumer, Ulbra.One.Accounting.Infrastructure.Messaging.AccountingReversalConsumerDefinition>();
            // AEL-848 — Contábil reclassifies a scheduled/paid payable's account: reverse+repost the incurral.
            x.AddConsumer<Ulbra.One.Accounting.Infrastructure.Messaging.PayableReclassifiedConsumer, Ulbra.One.Accounting.Infrastructure.Messaging.PayableReclassifiedConsumerDefinition>();
            // Contábil edits the payable's número da NF after posting (inclusive pago): refresh the SourceReference
            // (número no Livro Razão) do incurral + baixas já postados — amend de metadado, sem rebook.
            x.AddConsumer<Ulbra.One.Accounting.Infrastructure.Messaging.PayableDocumentNumberChangedConsumer, Ulbra.One.Accounting.Infrastructure.Messaging.PayableDocumentNumberChangedConsumerDefinition>();

            // AEL-793 — Requisição ↔ Finance: Finance creates the Payable when a requisition is approved;
            // Budgeting records the real "realizado" when a payment is registered/reversed.
            x.AddConsumer<Ulbra.One.Finance.Infrastructure.Messaging.RequisitionApprovedForPaymentConsumer>();
            // AEL-877 — recurring requisitions: each launched monthly cycle materializes one more Payable.
            x.AddConsumer<Ulbra.One.Finance.Infrastructure.Messaging.RequisitionCycleAddedForPaymentConsumer>();
            x.AddConsumer<Ulbra.One.Budgeting.Infrastructure.Messaging.RequisitionPaymentConsumer>();
            // AEL-877 — return link: Finance tells Budgeting which Payable it created (base or cycle) so the
            // requisition can list its generated Payables.
            x.AddConsumer<Ulbra.One.Budgeting.Infrastructure.Messaging.PayableRequisitionLinkConsumer>();
            // AEL-877 — a cancelled cycle Payable backs out its RequisitionCycle (stops counting toward
            // settlement; frees the competency for a relaunch).
            x.AddConsumer<Ulbra.One.Budgeting.Infrastructure.Messaging.CyclePayableCancelledConsumer>();
            // AEL-935 — a rejected requisition Payable returns the requisition to the requester/controllership;
            // publishes the transition (enrolled in the consumer outbox below for atomic publish-after-save).
            x.AddConsumer<Ulbra.One.Budgeting.Infrastructure.Messaging.PayableRejectedConsumer>();
            // AEL-865 — "Solicitar NF": Finance asks → Budgeting resolves the requester → Notifications pings them.
            x.AddConsumer<Ulbra.One.Budgeting.Infrastructure.Messaging.PayableFiscalDocumentRequestedConsumer>();
            // AEL-865 — self-service NF loop: an attachment added to a requisition is carried onto its Payable
            // when one exists (no-op otherwise). Idempotent by design; publishes RequisitionFiscalDocumentAttached
            // when the NF was requested, so it's enrolled in the consumer outbox below (atomic publish-after-save).
            x.AddConsumer<Ulbra.One.Finance.Infrastructure.Messaging.RequisitionAttachmentAddedConsumer>();
            // AEL-855 — cancelling a requisition cancels its still-open Payable (reverses the incurral).
            x.AddConsumer<Ulbra.One.Finance.Infrastructure.Messaging.RequisitionCancelledConsumer, Ulbra.One.Finance.Infrastructure.Messaging.RequisitionCancelledConsumerDefinition>();

            // AEL-794 — Notifications: turn esteira transitions into in-app notifications (idempotent).
            x.AddConsumer<Ulbra.One.Notifications.Infrastructure.Messaging.RequisitionTransitionedConsumer>();
            x.AddConsumer<Ulbra.One.Notifications.Infrastructure.Messaging.BudgetAdjustmentTransitionedConsumer>();
            x.AddConsumer<Ulbra.One.Notifications.Infrastructure.Messaging.RequisitionFiscalDocumentRequestedConsumer>();
            // AEL-865 — reverse of "Solicitar NF": pings the Contábil who asked once the requester attaches it.
            x.AddConsumer<Ulbra.One.Notifications.Infrastructure.Messaging.RequisitionFiscalDocumentAttachedConsumer>();
            // AEL-872 — active SLA escalation: Budgeting's periodic sweep publishes RequisitionSlaBreached; this
            // consumer materializes the role-targeted "SLA estourado" notification (idempotent on BreachId).
            x.AddConsumer<Ulbra.One.Notifications.Infrastructure.Messaging.RequisitionSlaBreachedConsumer>();
            // Realtime: closes the chain from a persisted notification to the browser — shared queue (one
            // replica processes) → PUBLISH on the Redis backplane, which fans out to every replica's SSE.
            x.AddConsumer<Ulbra.One.Notifications.Infrastructure.Messaging.NotificationCreatedConsumer>();

            x.AddEntityFrameworkOutbox<AppDbContext>(o =>
            {
                o.UsePostgres();
                o.UseBusOutbox();
                // How often the two sweepers ask the database whether there is anything to deliver
                // or clean up. The 1 s default is what integration tests need to stay deterministic
                // (a host that lives ~1 s must still deliver within it), but it is far tighter than
                // production needs: at 1 s it is ~172k queries a day, forever, whether or not anyone
                // is using the system. appsettings.Production.json relaxes it to 5 s — still well
                // under MassTransit's own ~10 s default, so delivery stays prompt on the esteira.
                o.QueryDelay = TimeSpan.FromSeconds(
                    configuration.GetValue("MassTransit:OutboxQueryDelaySeconds", 1d));
            });

            x.AddConfigureEndpointsCallback((context, name, cfg) =>
            {
                if (OutboxEnrolledEndpoints.Contains(name))
                    cfg.UseEntityFrameworkOutbox<AppDbContext>(context);
            });

            var transport = configuration["MassTransit:Transport"] ?? "RabbitMq";

            if (transport.Equals("AzureServiceBus", StringComparison.OrdinalIgnoreCase))
            {
                x.UsingAzureServiceBus((context, cfg) =>
                {
                    var connectionString = configuration["MassTransit:ConnectionString"];
                    if (!string.IsNullOrWhiteSpace(connectionString))
                    {
                        // Connection-string auth (e.g. local dev or non-MI environments)
                        cfg.Host(connectionString, _ => { });
                    }
                    else
                    {
                        // Passwordless via Managed Identity / DefaultAzureCredential
                        var ns = configuration["MassTransit:Namespace"]
                            ?? throw new InvalidOperationException(
                                "MassTransit:Namespace must be set when MassTransit:Transport=AzureServiceBus " +
                                "and MassTransit:ConnectionString is not provided.");
                        cfg.Host(new Uri($"sb://{ns}.servicebus.windows.net"), h =>
                        {
                            h.TokenCredential = new DefaultAzureCredential();
                        });
                    }

                    cfg.UseServiceBusMessageScheduler();
                    cfg.ConfigureEndpoints(context);
                });
            }
            else
            {
                x.UsingRabbitMq((context, cfg) =>
                {
                    var mt = configuration.GetSection("MassTransit");
                    var host = mt["Host"] ?? "localhost";
                    var portStr = mt["Port"];
                    var port = ushort.TryParse(portStr, out var p) ? p : (ushort)5672;
                    cfg.Host(host, port, "/", h =>
                    {
                        h.Username(mt["User"] ?? "guest");
                        h.Password(mt["Password"] ?? "guest");
                    });
                    // Delayed redelivery via the RabbitMQ delayed-message-exchange plugin (docker-compose
                    // provides it via rabbitmq.Dockerfile). The exchange is declared lazily on the first
                    // scheduled redelivery, so startup never fails when the plugin is absent.
                    cfg.UseDelayedMessageScheduler();
                    cfg.ConfigureEndpoints(context);
                });
            }
        });

        return services;
    }
}
