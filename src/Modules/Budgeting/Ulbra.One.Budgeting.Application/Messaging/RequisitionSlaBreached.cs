namespace Ulbra.One.Budgeting.Application.Messaging;

/// <summary>Published (EF bus outbox) by the periodic SLA sweep when a requisition's CURRENT Triage/Approval
/// stage has exceeded its business-hour SLA — the signal the Notifications module consumes to escalate to the
/// role that acts on that stage. Mirrors <see cref="RequisitionTransitioned"/> but is time-driven, not
/// transition-driven.
/// <para><see cref="BreachId"/> is a deterministic key over (requisition, stage, stage-entry) — the consumer's
/// idempotency key. The sweep re-runs periodically and re-publishes for still-overdue stages, so the same
/// occurrence notifies at most once; a re-entry into the stage (new <c>StatusChangedAt</c>) yields a fresh key
/// and can notify again.</para>
/// <see cref="Stage"/> travels as a string ("Triage"/"Approval") — neutral contract, like the other events.</summary>
public sealed record RequisitionSlaBreached(
    Guid BreachId,
    Guid RequisitionId,
    Guid CompanyId,
    int Code,
    string Stage,
    int ThresholdHours,
    DateTimeOffset OccurredAt);
