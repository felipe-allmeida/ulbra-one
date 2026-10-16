using MassTransit;
using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;

namespace Ulbra.One.Budgeting.Application.Messaging;

/// <summary>Publishes <see cref="RequisitionApprovedForPayment"/> exactly when a requisition has just entered
/// <c>InPayment</c> — the single "approved for payment" moment, reached by the Approver's approval
/// (AEL-841: no submit shortcuts). No-op otherwise, so callers invoke it unconditionally right after the
/// transition. Publish rides the EF bus outbox, so it commits atomically with the caller's <c>SaveChangesAsync</c>.</summary>
public static class RequisitionApprovalPublisher
{
    public static Task PublishIfApprovedForPayment(this Requisition requisition, IPublishEndpoint publish, CancellationToken ct) =>
        requisition.Status == RequisitionStatus.InPayment
            && requisition.Transitions.Count > 0
            && requisition.Transitions[^1].ToStatus == RequisitionStatus.InPayment
            ? publish.Publish(RequisitionApprovedForPayment.From(requisition), ct)
            : Task.CompletedTask;
}
