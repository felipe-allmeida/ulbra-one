using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Common.Domain;

namespace Ulbra.One.Budgeting.Application.Reports;

/// <summary>Pure SLA fold (no EF/IO). Live head scalars (<see cref="OpenStage"/>) drive the overdue-now queue;
/// the transition trail drives the historical analytics. Unit of measure = the stage OCCURRENCE per entry (the
/// live clock resets on each transition), so a re-entry counts twice. Window passed as pre-computed BRT instants.</summary>
public static class RequisitionSlaCalculator
{
    private static readonly RequisitionStatus[] SlaStages = [RequisitionStatus.Triage, RequisitionStatus.Approval];

    public static RequisitionSlaReport Build(
        IEnumerable<OpenStage> openStages,
        IEnumerable<RequisitionStageTransition> transitions,
        DateTimeOffset? fromInstant,
        DateTimeOffset? toExclusive,
        int triageThresholdHours,
        int approvalThresholdHours,
        DateTimeOffset now)
    {
        int Threshold(RequisitionStatus stage) =>
            stage == RequisitionStatus.Triage ? triageThresholdHours : approvalThresholdHours;

        bool InWindow(DateTimeOffset entry) =>
            (fromInstant is null || entry >= fromInstant) && (toExclusive is null || entry < toExclusive);

        // ---- Live: overdue right now, from the head scalar ----
        var overdue = new List<OverdueStage>();
        foreach (var o in openStages)
        {
            if (o.Stage is not (RequisitionStatus.Triage or RequisitionStatus.Approval)) continue;
            var elapsed = BusinessTime.BusinessHoursBetween(o.EnteredAt, now);
            var threshold = Threshold(o.Stage);
            if (elapsed > threshold)
                overdue.Add(new OverdueStage(o.RequisitionId, o.Code, o.Description, o.Stage, o.UnitId,
                    o.EnteredAt, elapsed, elapsed - threshold, o.EstimateValue));
        }
        overdue = overdue.OrderByDescending(x => x.BusinessHoursElapsed).ThenBy(x => x.Code).ToList();
        var live = new RequisitionSlaLive(
            overdue.Count(x => x.Stage == RequisitionStatus.Triage),
            overdue.Count(x => x.Stage == RequisitionStatus.Approval),
            overdue.Count,
            overdue.Count == 0 ? 0 : overdue.Max(x => x.BusinessHoursElapsed),
            overdue);

        // ---- Historical: stage occurrences + cycles, from the trail ----
        var occurrences = new List<StageOccurrence>();
        var cycles = new List<UnitHours>();

        foreach (var group in transitions.GroupBy(t => t.RequisitionId))
        {
            var ordered = group.OrderBy(t => t.OccurredAt).ToList();
            for (var i = 0; i < ordered.Count - 1; i++)
            {
                var entry = ordered[i];
                if (entry.ToStatus is not (RequisitionStatus.Triage or RequisitionStatus.Approval)) continue;
                if (!InWindow(entry.OccurredAt)) continue;
                var hours = BusinessTime.BusinessHoursBetween(entry.OccurredAt, ordered[i + 1].OccurredAt);
                occurrences.Add(new StageOccurrence(entry.UnitId, entry.ToStatus, hours, hours <= Threshold(entry.ToStatus)));
            }

            var submit = ordered.FirstOrDefault(t => t.Action == RequisitionAction.Submit);
            var approve = ordered.FirstOrDefault(t => t.Action == RequisitionAction.Approve);
            if (submit is not null && approve is not null && InWindow(approve.OccurredAt))
                cycles.Add(new UnitHours(approve.UnitId, BusinessTime.BusinessHoursBetween(submit.OccurredAt, approve.OccurredAt)));
        }

        var stages = SlaStages.Select(stage => BuildStageMetric(stage, occurrences, Threshold(stage))).ToList();
        var cycle = new CycleTimeMetric(cycles.Count, cycles.Count == 0 ? 0 : cycles.Average(c => c.Hours));
        var byUnit = BuildUnitMetrics(occurrences, cycles);

        return new RequisitionSlaReport(live, stages, cycle, byUnit);
    }

    private static StageSlaMetric BuildStageMetric(RequisitionStatus stage, List<StageOccurrence> all, int threshold)
    {
        var occ = all.Where(o => o.Stage == stage).ToList();
        var within = occ.Count(o => o.WithinSla);
        return new StageSlaMetric(
            stage, occ.Count,
            occ.Count == 0 ? 0 : occ.Average(o => o.Hours),
            within,
            occ.Count == 0 ? 0 : 100.0 * within / occ.Count,
            threshold);
    }

    private static List<UnitSlaMetric> BuildUnitMetrics(List<StageOccurrence> occurrences, List<UnitHours> cycles)
    {
        var unitIds = occurrences.Select(o => o.UnitId).Concat(cycles.Select(c => c.UnitId)).Distinct();
        return unitIds.Select(uid =>
        {
            var triage = occurrences.Where(o => o.UnitId == uid && o.Stage == RequisitionStatus.Triage).ToList();
            var approval = occurrences.Where(o => o.UnitId == uid && o.Stage == RequisitionStatus.Approval).ToList();
            var unitCycles = cycles.Where(c => c.UnitId == uid).ToList();
            return new UnitSlaMetric(
                uid,
                triage.Count, triage.Count == 0 ? 0 : triage.Average(o => o.Hours),
                triage.Count == 0 ? 0 : 100.0 * triage.Count(o => o.WithinSla) / triage.Count,
                approval.Count, approval.Count == 0 ? 0 : approval.Average(o => o.Hours),
                approval.Count == 0 ? 0 : 100.0 * approval.Count(o => o.WithinSla) / approval.Count,
                unitCycles.Count, unitCycles.Count == 0 ? 0 : unitCycles.Average(c => c.Hours));
        })
        .OrderByDescending(u => u.AvgCycleHours).ThenBy(u => u.UnitId)
        .ToList();
    }

    private sealed record StageOccurrence(Guid UnitId, RequisitionStatus Stage, int Hours, bool WithinSla);
    private sealed record UnitHours(Guid UnitId, int Hours);
}
