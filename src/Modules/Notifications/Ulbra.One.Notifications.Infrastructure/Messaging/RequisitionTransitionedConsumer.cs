using MassTransit;
using Ulbra.One.Budgeting.Application.Messaging;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Notifications.Application.Messaging;

namespace Ulbra.One.Notifications.Infrastructure.Messaging;

/// <summary>Turns a requisition esteira transition into an in-app notification (or none). Idempotent on the
/// transition id. The core is <see cref="ProcessAsync"/> so integration tests can drive it without a broker.</summary>
public sealed class RequisitionTransitionedConsumer(IUnitOfWork uow, IPublishEndpoint publish) : IConsumer<RequisitionTransitioned>
{
    public Task Consume(ConsumeContext<RequisitionTransitioned> context) =>
        ProcessAsync(context.Message, context.CancellationToken);

    public async Task ProcessAsync(RequisitionTransitioned message, CancellationToken ct)
    {
        var created = await NotificationWriter.PersistAsync(
            NotificationMapper.Map(message), message.TransitionId, uow, ct);
        // Published only on a real insert (never on a deduplicated redelivery) — this endpoint is enrolled in
        // MessagingSetup.OutboxEnrolledEndpoints, so the publish commits atomically with the row above.
        if (created is not null) await publish.Publish(
            new NotificationCreated(created.Id, created.AudienceUserId, created.AudienceRole), ct);
    }
}
