namespace Ulbra.One.Budgeting.Domain.Entities;

/// <summary>A single status transition of a <see cref="Requisition"/> — who moved it, when, and from→to.
/// Recorded on every state change (the chronological trail powering audit and future SLA). The actor is a
/// loose Guid into the Identity user (no cross-schema FK, like <see cref="RequisitionAttachment.UploadedById"/>).</summary>
public class RequisitionTransition
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid RequisitionId { get; private set; }
    public RequisitionStatus FromStatus { get; private set; }
    public RequisitionStatus ToStatus { get; private set; }
    public RequisitionAction Action { get; private set; }
    public Guid ActorId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string? Reason { get; private set; }

    private RequisitionTransition() { }

    public RequisitionTransition(
        Guid requisitionId, RequisitionStatus fromStatus, RequisitionStatus toStatus,
        RequisitionAction action, Guid actorId, DateTimeOffset occurredAt, string? reason)
    {
        RequisitionId = requisitionId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        Action = action;
        ActorId = actorId;
        OccurredAt = occurredAt;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }
}
