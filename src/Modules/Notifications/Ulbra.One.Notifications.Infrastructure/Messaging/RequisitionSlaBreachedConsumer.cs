using MassTransit;
using Ulbra.One.Budgeting.Application.Messaging;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Notifications.Application.Messaging;

namespace Ulbra.One.Notifications.Infrastructure.Messaging;

/// <summary>Turns a <see cref="RequisitionSlaBreached"/> event into a role-targeted "SLA estourado" notification
/// (Controladoria for Triage, Approver for Approval). Idempotent on <c>BreachId</c>. The core is
/// <see cref="ProcessAsync"/> so integration tests can drive it without a broker.</summary>
public sealed class RequisitionSlaBreachedConsumer(IUnitOfWork uow, IPublishEndpoint publish) : IConsumer<RequisitionSlaBreached>
{
    public Task Consume(ConsumeContext<RequisitionSlaBreached> context) =>
        ProcessAsync(context.Message, context.CancellationToken);

    public async Task ProcessAsync(RequisitionSlaBreached message, CancellationToken ct)
    {
        var created = await NotificationWriter.PersistAsync(
            NotificationMapper.Map(message), message.BreachId, uow, ct);
        // Published only on a real insert (never on a deduplicated redelivery) — this endpoint is enrolled in
        // MessagingSetup.OutboxEnrolledEndpoints, so the publish commits atomically with the row above.
        if (created is not null) await publish.Publish(
            new NotificationCreated(created.Id, created.AudienceUserId, created.AudienceRole), ct);
    }
}
