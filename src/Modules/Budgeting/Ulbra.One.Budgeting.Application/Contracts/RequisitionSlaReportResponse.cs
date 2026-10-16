using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Budgeting.Application.Reports;
using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Budgeting.Application.Contracts;

/// <summary>One requisition currently overdue in its stage (the live overdue queue).</summary>
[SwaggerSchema("A requisition that is overdue in its current stage, right now. Ages are counted in "
    + "BUSINESS hours, so a weekend does not push a requisition past its SLA.")]
public sealed record OverdueStageResponse(
    RequisitionId RequisitionId,
    [property: SwaggerSchema("Per-company sequential code.")]
    [property: SwaggerExample("482")]
    int Code,
    [property: SwaggerSchema("What was requested.")]
    [property: SwaggerExample("Manutenção preventiva do ar-condicionado do laboratório")]
    string Description,
    [property: SwaggerSchema("The stage it is stuck in.")]
    RequisitionStatus Stage,
    [property: SwaggerSchema("Unit the requisition belongs to.")]
    UnitId UnitId,
    [property: SwaggerSchema("When it entered the stage — the clock's start.")]
    DateTimeOffset EnteredAt,
    [property: SwaggerSchema("Business hours since it entered the stage.")]
    [property: SwaggerExample("62")]
    int BusinessHoursElapsed,
    [property: SwaggerSchema("Business hours past the stage's threshold. Always positive here, since only "
        + "overdue rows are listed.")]
    [property: SwaggerExample("14")]
    int OverdueByHours,
    [property: SwaggerSchema("Value at stake, in BRL — used to sort the queue by what costs most to "
        + "leave waiting.")]
    [property: SwaggerExample("12500.00")]
    decimal EstimateValue)
{
    public static OverdueStageResponse From(OverdueStage o) => new(
        new RequisitionId(o.RequisitionId), o.Code, o.Description, o.Stage, new UnitId(o.UnitId),
        o.EnteredAt, o.BusinessHoursElapsed, o.OverdueByHours, o.EstimateValue);
}

/// <summary>Live section: overdue counts right now + the queue rows.</summary>
[SwaggerSchema("What is overdue right now. A live snapshot, unlike the historical sections — it "
    + "changes as requisitions move, not as the date range changes.")]
public sealed record RequisitionSlaLiveResponse(
    [property: SwaggerSchema("Requisitions overdue in triage.")]
    [property: SwaggerExample("7")]
    int TriageOverdue,
    [property: SwaggerSchema("Requisitions overdue in approval.")]
    [property: SwaggerExample("4")]
    int ApprovalOverdue,
    [property: SwaggerSchema("Total overdue across the stages.")]
    [property: SwaggerExample("11")]
    int TotalOverdue,
    [property: SwaggerSchema("Business hours the oldest overdue requisition has been waiting.")]
    [property: SwaggerExample("96")]
    int OldestOverdueBusinessHours,
    [property: SwaggerSchema("The overdue requisitions themselves.")]
    IReadOnlyList<OverdueStageResponse> Rows)
{
    public static RequisitionSlaLiveResponse From(RequisitionSlaLive l) => new(
        l.TriageOverdue, l.ApprovalOverdue, l.TotalOverdue, l.OldestOverdueBusinessHours,
        l.Rows.Select(OverdueStageResponse.From).ToList());
}

/// <summary>Historical aggregate for one stage.</summary>
[SwaggerSchema("How one stage performed over the reported range — history, not the live queue.")]
public sealed record StageSlaMetricResponse(
    [property: SwaggerSchema("The stage being measured.")]
    RequisitionStatus Stage,
    [property: SwaggerSchema("How many times the stage was traversed in the range. A requisition returned "
        + "and re-submitted counts more than once.")]
    [property: SwaggerExample("214")]
    int Occurrences,
    [property: SwaggerSchema("Average business hours spent in the stage.")]
    [property: SwaggerExample("18.4")]
    double AverageBusinessHours,
    [property: SwaggerSchema("How many traversals stayed within the threshold.")]
    [property: SwaggerExample("188")]
    int WithinSlaCount,
    [property: SwaggerSchema("Percentage of traversals within the threshold.")]
    [property: SwaggerExample("87.9")]
    double WithinSlaPercent,
    [property: SwaggerSchema("The threshold this stage was measured against, in business hours.")]
    [property: SwaggerExample("24")]
    int ThresholdHours)
{
    public static StageSlaMetricResponse From(StageSlaMetric s) => new(
        s.Stage, s.Occurrences, s.AverageBusinessHours, s.WithinSlaCount, s.WithinSlaPercent, s.ThresholdHours);
}

[SwaggerSchema("End-to-end time from submission to the final decision, over the reported range.")]
public sealed record CycleTimeResponse(
    [property: SwaggerSchema("How many requisitions completed a full cycle in the range.")]
    [property: SwaggerExample("196")]
    int Count,
    [property: SwaggerSchema("Average business hours from submission to final decision.")]
    [property: SwaggerExample("41.7")]
    double AverageBusinessHours)
{
    public static CycleTimeResponse From(CycleTimeMetric c) => new(c.Count, c.AverageBusinessHours);
}

/// <summary>Per-unit row of the ranking.</summary>
[SwaggerSchema("One unit's row of the SLA ranking — the same metrics, sliced by unit.")]
public sealed record UnitSlaMetricResponse(
    UnitId UnitId,
    [property: SwaggerSchema("Triage traversals in the range for this unit.")]
    [property: SwaggerExample("48")]
    int TriageOccurrences,
    [property: SwaggerSchema("Average business hours in triage.")]
    [property: SwaggerExample("16.2")]
    double AvgTriageHours,
    [property: SwaggerSchema("Percentage of triage traversals within the threshold.")]
    [property: SwaggerExample("91.7")]
    double TriageWithinSlaPercent,
    [property: SwaggerSchema("Approval traversals in the range for this unit.")]
    [property: SwaggerExample("44")]
    int ApprovalOccurrences,
    [property: SwaggerSchema("Average business hours in approval.")]
    [property: SwaggerExample("22.9")]
    double AvgApprovalHours,
    [property: SwaggerSchema("Percentage of approval traversals within the threshold.")]
    [property: SwaggerExample("84.1")]
    double ApprovalWithinSlaPercent,
    [property: SwaggerSchema("Requisitions that completed a full cycle in this unit.")]
    [property: SwaggerExample("41")]
    int CycleCount,
    [property: SwaggerSchema("Average business hours end to end.")]
    [property: SwaggerExample("39.8")]
    double AvgCycleHours)
{
    public static UnitSlaMetricResponse From(UnitSlaMetric u) => new(
        new UnitId(u.UnitId), u.TriageOccurrences, u.AvgTriageHours, u.TriageWithinSlaPercent,
        u.ApprovalOccurrences, u.AvgApprovalHours, u.ApprovalWithinSlaPercent, u.CycleCount, u.AvgCycleHours);
}

/// <summary>The SLA report for a company (or consolidated when <paramref name="CompanyId"/> is null):
/// live overdue queue + historical analytics. Echoes the requested filters.</summary>
[SwaggerSchema("The requisition SLA report: what is overdue right now plus how the stages performed "
    + "over a range. The live section ignores the date filter — it is always the present.")]
public sealed record RequisitionSlaReportResponse(
    [property: SwaggerSchema("Company the report covers. Null means consolidated.")]
    CompanyId? CompanyId,
    [property: SwaggerSchema("Unit the report was filtered to. Null means every unit.")]
    UnitId? UnitId,
    [property: SwaggerSchema("Start of the historical range. Null means from the beginning.")]
    [property: SwaggerExample("2026-01-01")]
    DateOnly? From,
    [property: SwaggerSchema("End of the historical range, inclusive.")]
    [property: SwaggerExample("2026-07-31")]
    DateOnly? To,
    [property: SwaggerSchema("When the report was computed. It matters because the live section is a "
        + "snapshot at this instant.")]
    DateTimeOffset GeneratedAt,
    [property: SwaggerSchema("Triage threshold used, in business hours.")]
    [property: SwaggerExample("24")]
    int TriageThresholdHours,
    [property: SwaggerSchema("Approval threshold used, in business hours.")]
    [property: SwaggerExample("48")]
    int ApprovalThresholdHours,
    [property: SwaggerSchema("What is overdue right now, regardless of the date range.")]
    RequisitionSlaLiveResponse Live,
    [property: SwaggerSchema("Historical performance per stage.")]
    IReadOnlyList<StageSlaMetricResponse> Stages,
    [property: SwaggerSchema("End-to-end cycle time.")]
    CycleTimeResponse Cycle,
    [property: SwaggerSchema("The same metrics sliced by unit, for the ranking.")]
    IReadOnlyList<UnitSlaMetricResponse> ByUnit)
{
    public static RequisitionSlaReportResponse Of(
        RequisitionSlaReport r, CompanyId? companyId, UnitId? unitId, DateOnly? from, DateOnly? to,
        DateTimeOffset generatedAt, int triageThresholdHours, int approvalThresholdHours) => new(
        companyId, unitId, from, to, generatedAt, triageThresholdHours, approvalThresholdHours,
        RequisitionSlaLiveResponse.From(r.Live),
        r.Stages.Select(StageSlaMetricResponse.From).ToList(),
        CycleTimeResponse.From(r.Cycle),
        r.ByUnit.Select(UnitSlaMetricResponse.From).ToList());
}
