using Ulbra.One.Budgeting.Application.Reports;
using Ulbra.One.Budgeting.Domain;
using Xunit;

namespace Ulbra.One.UnitTests.Budgeting;

/// <summary>The pure SLA calculator: derives per-stage durations from the transition trail (per stage
/// OCCURRENCE, so re-entries count twice), the live-overdue queue from the head scalar, and the per-unit
/// ranking. Business-hours come from <see cref="BusinessTime"/> (BRT, holidays).</summary>
public class RequisitionSlaCalculatorTests
{
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid UnitA = Guid.NewGuid();
    private static readonly Guid UnitB = Guid.NewGuid();

    private const int Triage24 = 24;
    private const int Approval24 = 24;

    // BRT wall-clock. Jan 5 2026 is a Monday.
    private static DateTimeOffset At(int day, int hour) => new(2026, 1, day, hour, 0, 0, TimeSpan.FromHours(-3));

    private static RequisitionStageTransition Tr(
        Guid req, Guid unit, RequisitionStatus from, RequisitionStatus to, RequisitionAction action, DateTimeOffset at) =>
        new(req, Company, unit, from, to, action, at);

    private static OpenStage Open(Guid req, RequisitionStatus stage, DateTimeOffset enteredAt, Guid? unit = null) =>
        new(req, 1, Company, unit ?? UnitA, "req", stage, enteredAt, 100m);

    [Fact]
    public void Empty_input_yields_zeroed_report()
    {
        var r = RequisitionSlaCalculator.Build([], [], null, null, Triage24, Approval24, At(5, 12));

        Assert.Equal(0, r.Live.TotalOverdue);
        Assert.Empty(r.Live.Rows);
        Assert.Equal(0, r.Cycle.Count);
        Assert.Empty(r.ByUnit);
        // Stages always lists both SLA stages, with zero occurrences.
        Assert.Equal(2, r.Stages.Count);
        Assert.All(r.Stages, s => Assert.Equal(0, s.Occurrences));
    }

    [Fact]
    public void Single_clean_pass_measures_triage_approval_and_cycle()
    {
        var req = Guid.NewGuid();
        var trail = new[]
        {
            Tr(req, UnitA, RequisitionStatus.Draft, RequisitionStatus.Triage, RequisitionAction.Submit, At(5, 9)),
            Tr(req, UnitA, RequisitionStatus.Triage, RequisitionStatus.FinancialComplement, RequisitionAction.ApproveTriage, At(5, 14)),
            Tr(req, UnitA, RequisitionStatus.FinancialComplement, RequisitionStatus.Approval, RequisitionAction.SubmitFinancialComplement, At(5, 15)),
            Tr(req, UnitA, RequisitionStatus.Approval, RequisitionStatus.InPayment, RequisitionAction.Approve, At(5, 18)),
        };

        var r = RequisitionSlaCalculator.Build([], trail, null, null, Triage24, Approval24, At(5, 20));

        var triage = Assert.Single(r.Stages, s => s.Stage == RequisitionStatus.Triage);
        Assert.Equal(1, triage.Occurrences);
        Assert.Equal(5, triage.AverageBusinessHours);   // 09:00 → 14:00
        Assert.Equal(100, triage.WithinSlaPercent);

        var approval = Assert.Single(r.Stages, s => s.Stage == RequisitionStatus.Approval);
        Assert.Equal(1, approval.Occurrences);
        Assert.Equal(3, approval.AverageBusinessHours); // 15:00 → 18:00

        Assert.Equal(1, r.Cycle.Count);
        Assert.Equal(9, r.Cycle.AverageBusinessHours);  // 09:00 → 18:00
    }

    [Fact]
    public void Re_entry_counts_two_triage_occurrences()
    {
        var req = Guid.NewGuid();
        var trail = new[]
        {
            Tr(req, UnitA, RequisitionStatus.Draft, RequisitionStatus.Triage, RequisitionAction.Submit, At(5, 8)),
            Tr(req, UnitA, RequisitionStatus.Triage, RequisitionStatus.FinancialComplement, RequisitionAction.ApproveTriage, At(5, 12)),          // Triage #1 = 4h
            Tr(req, UnitA, RequisitionStatus.FinancialComplement, RequisitionStatus.Triage, RequisitionAction.ReturnToTriageForRevalue, At(5, 13)),
            Tr(req, UnitA, RequisitionStatus.Triage, RequisitionStatus.FinancialComplement, RequisitionAction.ApproveTriage, At(5, 19)),          // Triage #2 = 6h
            Tr(req, UnitA, RequisitionStatus.FinancialComplement, RequisitionStatus.Approval, RequisitionAction.SubmitFinancialComplement, At(5, 20)),
            Tr(req, UnitA, RequisitionStatus.Approval, RequisitionStatus.InPayment, RequisitionAction.Approve, At(5, 22)),                          // Approval = 2h
        };

        var r = RequisitionSlaCalculator.Build([], trail, null, null, Triage24, Approval24, At(6, 9));

        var triage = Assert.Single(r.Stages, s => s.Stage == RequisitionStatus.Triage);
        Assert.Equal(2, triage.Occurrences);
        Assert.Equal(5, triage.AverageBusinessHours); // (4 + 6) / 2
    }

    [Fact]
    public void Within_sla_percent_uses_threshold_boundary_as_within()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        // Threshold 4h: A takes exactly 4h (within, boundary), B takes 6h (breach).
        var trail = new[]
        {
            Tr(a, UnitA, RequisitionStatus.Draft, RequisitionStatus.Triage, RequisitionAction.Submit, At(5, 8)),
            Tr(a, UnitA, RequisitionStatus.Triage, RequisitionStatus.FinancialComplement, RequisitionAction.ApproveTriage, At(5, 12)),
            Tr(b, UnitA, RequisitionStatus.Draft, RequisitionStatus.Triage, RequisitionAction.Submit, At(5, 8)),
            Tr(b, UnitA, RequisitionStatus.Triage, RequisitionStatus.FinancialComplement, RequisitionAction.ApproveTriage, At(5, 14)),
        };

        var r = RequisitionSlaCalculator.Build([], trail, null, null, triageThresholdHours: 4, approvalThresholdHours: 24, At(6, 9));

        var triage = Assert.Single(r.Stages, s => s.Stage == RequisitionStatus.Triage);
        Assert.Equal(2, triage.Occurrences);
        Assert.Equal(1, triage.WithinSlaCount);
        Assert.Equal(50, triage.WithinSlaPercent);
    }

    [Fact]
    public void Open_occurrence_is_excluded_from_history_but_surfaces_as_live_overdue()
    {
        var req = Guid.NewGuid();
        // Still in Triage: only the entry transition, no exit → open.
        var trail = new[]
        {
            Tr(req, UnitA, RequisitionStatus.Draft, RequisitionStatus.Triage, RequisitionAction.Submit, At(5, 8)),
        };
        var open = new[] { Open(req, RequisitionStatus.Triage, At(5, 8)) };

        var r = RequisitionSlaCalculator.Build(open, trail, null, null, Triage24, Approval24, At(12, 12));

        var triage = Assert.Single(r.Stages, s => s.Stage == RequisitionStatus.Triage);
        Assert.Equal(0, triage.Occurrences);          // open occurrence not counted historically
        Assert.Equal(1, r.Live.TotalOverdue);         // but it is overdue now
        Assert.Equal(1, r.Live.TriageOverdue);
        Assert.Equal(req, Assert.Single(r.Live.Rows).RequisitionId);
    }

    [Fact]
    public void Occurrence_entered_before_window_is_excluded()
    {
        var req = Guid.NewGuid();
        var trail = new[]
        {
            Tr(req, UnitA, RequisitionStatus.Draft, RequisitionStatus.Triage, RequisitionAction.Submit, At(5, 8)),
            Tr(req, UnitA, RequisitionStatus.Triage, RequisitionStatus.FinancialComplement, RequisitionAction.ApproveTriage, At(5, 12)),
        };

        var windowed = RequisitionSlaCalculator.Build([], trail, fromInstant: At(6, 0), toExclusive: null, Triage24, Approval24, At(9, 9));
        Assert.Equal(0, Assert.Single(windowed.Stages, s => s.Stage == RequisitionStatus.Triage).Occurrences);

        var unbounded = RequisitionSlaCalculator.Build([], trail, null, null, Triage24, Approval24, At(9, 9));
        Assert.Equal(1, Assert.Single(unbounded.Stages, s => s.Stage == RequisitionStatus.Triage).Occurrences);
    }

    [Fact]
    public void Groups_metrics_by_unit()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var trail = new[]
        {
            // UnitA: triage 4h
            Tr(a, UnitA, RequisitionStatus.Draft, RequisitionStatus.Triage, RequisitionAction.Submit, At(5, 8)),
            Tr(a, UnitA, RequisitionStatus.Triage, RequisitionStatus.FinancialComplement, RequisitionAction.ApproveTriage, At(5, 12)),
            // UnitB: triage 6h
            Tr(b, UnitB, RequisitionStatus.Draft, RequisitionStatus.Triage, RequisitionAction.Submit, At(5, 8)),
            Tr(b, UnitB, RequisitionStatus.Triage, RequisitionStatus.FinancialComplement, RequisitionAction.ApproveTriage, At(5, 14)),
        };

        var r = RequisitionSlaCalculator.Build([], trail, null, null, Triage24, Approval24, At(6, 9));

        Assert.Equal(2, r.ByUnit.Count);
        Assert.Equal(4, Assert.Single(r.ByUnit, u => u.UnitId == UnitA).AvgTriageHours);
        Assert.Equal(6, Assert.Single(r.ByUnit, u => u.UnitId == UnitB).AvgTriageHours);
    }
}
