namespace Ulbra.One.Budgeting.Domain.Entities;

/// <summary>A single status transition of an <see cref="BudgetAdjustment"/> — who moved it, when, and
/// from→to. Recorded on every state change (the chronological trail powering audit and the stage SLA).
/// The actor is a loose Guid into the Identity user (no cross-schema FK). Mirrors
/// <see cref="RequisitionTransition"/>.</summary>
public class BudgetAdjustmentTransition
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid BudgetAdjustmentId { get; private set; }
    public BudgetAdjustmentStatus FromStatus { get; private set; }
    public BudgetAdjustmentStatus ToStatus { get; private set; }
    public BudgetAdjustmentAction Action { get; private set; }
    public Guid ActorId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string? Reason { get; private set; }

    private BudgetAdjustmentTransition() { }

    public BudgetAdjustmentTransition(
        Guid adequacaoId, BudgetAdjustmentStatus fromStatus, BudgetAdjustmentStatus toStatus,
        BudgetAdjustmentAction action, Guid actorId, DateTimeOffset occurredAt, string? reason)
    {
        BudgetAdjustmentId = adequacaoId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        Action = action;
        ActorId = actorId;
        OccurredAt = occurredAt;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }
}
