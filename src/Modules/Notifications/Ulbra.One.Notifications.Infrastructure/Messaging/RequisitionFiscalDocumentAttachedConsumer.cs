using MassTransit;
using Ulbra.One.Budgeting.Application.Messaging;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Notifications.Application.Messaging;

namespace Ulbra.One.Notifications.Infrastructure.Messaging;

/// <summary>Turns "the requester attached the NF" into a personal in-app notification for the Contábil user who
/// requested it. Idempotent on the PayableId (one notification per payable). The core is <see cref="ProcessAsync"/>
/// so integration tests can drive it without a broker.</summary>
public sealed class RequisitionFiscalDocumentAttachedConsumer(IUnitOfWork uow, IPublishEndpoint publish) : IConsumer<RequisitionFiscalDocumentAttached>
{
    public Task Consume(ConsumeContext<RequisitionFiscalDocumentAttached> context) =>
        ProcessAsync(context.Message, context.CancellationToken);

    public async Task ProcessAsync(RequisitionFiscalDocumentAttached message, CancellationToken ct)
    {
        var created = await NotificationWriter.PersistAsync(
            NotificationMapper.Map(message), message.PayableId, uow, ct);
        // Published only on a real insert (never on a deduplicated redelivery) — this endpoint is enrolled in
        // MessagingSetup.OutboxEnrolledEndpoints, so the publish commits atomically with the row above.
        if (created is not null) await publish.Publish(
            new NotificationCreated(created.Id, created.AudienceUserId, created.AudienceRole), ct);
    }
}
