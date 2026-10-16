using Ulbra.One.Budgeting.Domain;

namespace Ulbra.One.Budgeting.Application.Reports;

// Flat inputs the pure SLA calculator consumes (primitives only — no EF, no entities), mirroring
// Accounting's PostedLine. Two feeds: the live head scalar and the historical transition trail.

/// <summary>A requisition currently sitting in an SLA stage (Triage/Approval), from the head scalar
/// <c>StatusChangedAt</c> — the live-overdue feed (no trail load needed).</summary>
public sealed record OpenStage(
    Guid RequisitionId, int Code, Guid CompanyId, Guid UnitId, string Description,
    RequisitionStatus Stage, DateTimeOffset EnteredAt, decimal EstimateValue);

/// <summary>One row of the transition trail, joined with its requisition's unit — the historical feed.
/// The stage a requisition entered is <see cref="ToStatus"/> at <see cref="OccurredAt"/>; it left when the
/// next transition occurred.</summary>
public sealed record RequisitionStageTransition(
    Guid RequisitionId, Guid CompanyId, Guid UnitId,
    RequisitionStatus FromStatus, RequisitionStatus ToStatus,
    RequisitionAction Action, DateTimeOffset OccurredAt);

// Calculator outputs.

/// <summary>A stage occurrence currently past its SLA (the live overdue queue).</summary>
public sealed record OverdueStage(
    Guid RequisitionId, int Code, string Description, RequisitionStatus Stage, Guid UnitId,
    DateTimeOffset EnteredAt, int BusinessHoursElapsed, int OverdueByHours, decimal EstimateValue);

/// <summary>Aggregate performance of one stage over the historical window. Unit of measure is the stage
/// occurrence (per entry) — a requisition that re-entered a stage contributes multiple occurrences.</summary>
public sealed record StageSlaMetric(
    RequisitionStatus Stage, int Occurrences, double AverageBusinessHours,
    int WithinSlaCount, double WithinSlaPercent, int ThresholdHours);

/// <summary>End-to-end lead time (Submit → Approve) over the window; includes requester-held stages.</summary>
public sealed record CycleTimeMetric(int Count, double AverageBusinessHours);

/// <summary>Per-unit breakdown for the ranking (slowest units first, by average cycle time).</summary>
public sealed record UnitSlaMetric(
    Guid UnitId,
    int TriageOccurrences, double AvgTriageHours, double TriageWithinSlaPercent,
    int ApprovalOccurrences, double AvgApprovalHours, double ApprovalWithinSlaPercent,
    int CycleCount, double AvgCycleHours);

/// <summary>The live section: how many stage occurrences are overdue right now, plus the queue rows.</summary>
public sealed record RequisitionSlaLive(
    int TriageOverdue, int ApprovalOverdue, int TotalOverdue, int OldestOverdueBusinessHours,
    IReadOnlyList<OverdueStage> Rows);

/// <summary>The full SLA report: live overdue queue + historical analytics.</summary>
public sealed record RequisitionSlaReport(
    RequisitionSlaLive Live,
    IReadOnlyList<StageSlaMetric> Stages,
    CycleTimeMetric Cycle,
    IReadOnlyList<UnitSlaMetric> ByUnit);
