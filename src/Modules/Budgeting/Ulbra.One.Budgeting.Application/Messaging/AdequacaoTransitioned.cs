using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;

namespace Ulbra.One.Budgeting.Application.Messaging;

/// <summary>Published (EF bus outbox) on every budget-adjustment status transition — the Notifications
/// counterpart of <see cref="RequisitionTransitioned"/> for the adequação esteira (send-to-approval /
/// approve / cancel / reverse), plus a synthetic "created" event (see <see cref="ForCreation"/>) because an
/// adequação is born in <c>AwaitingTriage</c> without a transition row. <see cref="TransitionId"/> is the
/// consumer's idempotency key (the persisted transition id, or the adequação id for the synthetic create).</summary>
public sealed record BudgetAdjustmentTransitioned(
    Guid TransitionId,
    Guid BudgetAdjustmentId,
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
    /// <summary>Builds the event from the adequação's most recent transition (send-to-approval / approve /
    /// cancel / reverse). Requires the caller to have loaded <c>Transitions</c> and appended the new row.</summary>
    public static BudgetAdjustmentTransitioned From(BudgetAdjustment a)
    {
        var t = a.Transitions[^1];
        return new BudgetAdjustmentTransitioned(
            t.Id, a.Id, a.CompanyId, a.Code,
            t.FromStatus.ToString(), t.ToStatus.ToString(), t.Action.ToString(),
            t.ActorId, TransitionParticipants.RequesterFrom(a.CreatedBy), t.Reason, t.OccurredAt);
    }

    /// <summary>Synthetic event for the creation of an adequação — it starts in <c>AwaitingTriage</c> with no
    /// transition row, so the triage notification would otherwise never fire. Uses the adequação id as the
    /// idempotency key (there is exactly one creation per adequação) and stamps the actor from the creator.</summary>
    public static BudgetAdjustmentTransitioned ForCreation(BudgetAdjustment a, DateTimeOffset occurredAt)
    {
        var requester = TransitionParticipants.RequesterFrom(a.CreatedBy);
        return new BudgetAdjustmentTransitioned(
            a.Id, a.Id, a.CompanyId, a.Code,
            BudgetAdjustmentStatus.AwaitingTriage.ToString(), BudgetAdjustmentStatus.AwaitingTriage.ToString(), "Create",
            requester ?? Guid.Empty, requester, null, occurredAt);
    }
}
