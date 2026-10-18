using Ulbra.One.Budgeting.Domain.Entities;

namespace Ulbra.One.Budgeting.Application.Messaging;

/// <summary>Published (EF bus outbox) on every requisition status transition — the generic "the esteira
/// moved" signal the Notifications module consumes to notify the right audience. Distinct from
/// <see cref="RequisitionApprovedForPayment"/> (that one is the Finance integration, fired only on
/// InPayment); this fires on every transition (submit / approve-triage / submit-financial-complement /
/// return-for-complement / resubmit / approve / cancel), so both may be published in the same handler when a
/// requisition reaches InPayment.
/// <para><see cref="TransitionId"/> is the persisted <see cref="RequisitionTransition.Id"/> (client-generated,
/// available before the save) — the consumer's idempotency key, so a legitimately repeated transition
/// (e.g. return-for-complement twice) produces two distinct notifications while a redelivery of the same
/// transition is deduplicated.</para>
/// Enums travel as strings (neutral contract, mirroring <c>PayableType</c> in
/// <see cref="RequisitionApprovedForPayment"/>). <see cref="RequesterId"/> is the solicitante resolved from
/// the aggregate's <c>CreatedBy</c>; <c>null</c> when it wasn't an authenticated user ("system").</summary>
public sealed record RequisitionTransitioned(
    Guid TransitionId,
    Guid RequisitionId,
    Guid CompanyId,
    int Code,
    string FromStatus,
    string ToStatus,
    string Action,
    Guid ActorId,
    Guid? RequesterId,
    string? Reason,
    DateTimeOffset OccurredAt)
{
    /// <summary>Builds the event from the requisition's most recent transition. The caller must have loaded
    /// <c>Transitions</c> (every transition endpoint Includes it) and just appended the new row.</summary>
    public static RequisitionTransitioned From(Requisition r)
    {
        var t = r.Transitions[^1];
        return new RequisitionTransitioned(
            t.Id, r.Id, r.CompanyId, r.Code,
            t.FromStatus.ToString(), t.ToStatus.ToString(), t.Action.ToString(),
            t.ActorId, TransitionParticipants.RequesterFrom(r.CreatedBy), t.Reason, t.OccurredAt);
    }
}
