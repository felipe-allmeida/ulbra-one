namespace Ulbra.One.Budgeting.Application;

/// <summary>Per-stage SLA thresholds (business hours), bound from the <c>RequisitionSla</c> configuration
/// section. Consumed by the report endpoint and the escalation sweep to decide when a stage is overdue.</summary>
public sealed class RequisitionSlaOptions
{
    public const string SectionName = "RequisitionSla";

    /// <summary>Business-hours a requisition may sit in Triage before it is overdue.</summary>
    public int TriageHours { get; set; } = 24;

    /// <summary>Business-hours a requisition may sit in Approval before it is overdue.</summary>
    public int ApprovalHours { get; set; } = 24;
}
