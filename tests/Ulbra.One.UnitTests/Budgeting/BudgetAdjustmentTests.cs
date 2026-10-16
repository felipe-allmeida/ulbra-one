using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;
using Xunit;

namespace Ulbra.One.UnitTests.Budgeting;

public class BudgetAdjustmentTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch;

    private static BudgetAdjustment New(
        BudgetAdjustmentType type = BudgetAdjustmentType.Supplementation,
        string competency = "2026-07",
        string? sourceCompetency = null,
        decimal value = 100m,
        string justification = "Justificativa",
        string? counterparts = "Entregas",
        int code = 1) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), code, type, competency, sourceCompetency, value, justification, counterparts);

    private static void AssertInvalidTransition(Action act)
    {
        var ex = Assert.Throws<BudgetAdjustmentException>(act);
        Assert.Equal(BudgetAdjustmentErrors.InvalidTransition.Code, ex.Error.Code);
    }

    private static void AssertError(Error expected, Action act)
    {
        var ex = Assert.Throws<BudgetAdjustmentException>(act);
        Assert.Equal(expected.Code, ex.Error.Code);
    }

    // ---- Construction ----------------------------------------------------------------------------

    [Fact]
    public void Constructor_sets_fields()
    {
        var company = Guid.NewGuid();
        var unit = Guid.NewGuid();
        var costCenter = Guid.NewGuid();
        var a = new BudgetAdjustment(company, unit, costCenter, 42, BudgetAdjustmentType.Transfer,
            "2026-08", "2026-07", 1234.56m, "Justificativa", "Contrapartidas");

        Assert.Equal(company, a.CompanyId);
        Assert.Equal(unit, a.UnitId);
        Assert.Equal(costCenter, a.CostCenterId);
        Assert.Equal(42, a.Code);
        Assert.Equal(BudgetAdjustmentType.Transfer, a.Type);
        Assert.Equal("2026-08", a.Competency);
        Assert.Equal("2026-07", a.SourceCompetency);
        Assert.Equal(1234.56m, a.Value);
        Assert.Equal("Justificativa", a.Justification);
        Assert.Equal("Contrapartidas", a.Counterparts);
    }

    [Fact]
    public void New_adjustment_starts_awaiting_triage() => Assert.Equal(BudgetAdjustmentStatus.AwaitingTriage, New().Status);

    [Fact]
    public void Constructor_trims_justification_and_counterparts()
    {
        var a = New(justification: "  just  ", counterparts: "  cp  ");
        Assert.Equal("just", a.Justification);
        Assert.Equal("cp", a.Counterparts);
    }

    [Fact]
    public void Constructor_rounds_value_to_two_decimals() =>
        Assert.Equal(100.13m, New(value: 100.125m).Value); // AwayFromZero

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_rejects_non_positive_value(decimal value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => New(value: value));

    [Fact]
    public void Constructor_requires_justification() =>
        Assert.Throws<ArgumentException>(() => New(justification: "  "));

    [Theory]
    [InlineData("2026-13")]
    [InlineData("202607")]
    [InlineData("")]
    public void Constructor_rejects_invalid_competency(string competency) =>
        Assert.Throws<ArgumentException>(() => New(competency: competency));

    [Fact]
    public void Constructor_rejects_code_below_one() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => New(code: 0));

    [Fact]
    public void Constructor_rejects_empty_ids()
    {
        Assert.Throws<ArgumentException>(() => new BudgetAdjustment(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), 1, BudgetAdjustmentType.Supplementation, "2026-07", null, 1m, "j", "c"));
        Assert.Throws<ArgumentException>(() => new BudgetAdjustment(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), 1, BudgetAdjustmentType.Supplementation, "2026-07", null, 1m, "j", "c"));
        Assert.Throws<ArgumentException>(() => new BudgetAdjustment(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, 1, BudgetAdjustmentType.Supplementation, "2026-07", null, 1m, "j", "c"));
    }

    // ---- Type-specific rules (counterparts + transfer source) ------------------------------------

    [Fact]
    public void Supplementation_requires_counterparts() =>
        AssertThrowsArgument("counterparts", () => New(type: BudgetAdjustmentType.Supplementation, counterparts: null));

    [Fact]
    public void Reduction_allows_null_counterparts()
    {
        var a = New(type: BudgetAdjustmentType.Reduction, counterparts: null);
        Assert.Null(a.Counterparts);
    }

    [Fact]
    public void Transfer_requires_source_competency() =>
        AssertError(BudgetAdjustmentErrors.SourceRequired, () => New(type: BudgetAdjustmentType.Transfer, sourceCompetency: null));

    [Fact]
    public void Transfer_source_must_differ_from_destination() =>
        AssertError(BudgetAdjustmentErrors.SourceEqualsDestination,
            () => New(type: BudgetAdjustmentType.Transfer, competency: "2026-07", sourceCompetency: "2026-07"));

    [Fact]
    public void Non_transfer_rejects_a_source_competency() =>
        AssertError(BudgetAdjustmentErrors.SourceNotAllowed,
            () => New(type: BudgetAdjustmentType.Supplementation, sourceCompetency: "2026-06"));

    [Fact]
    public void Valid_transfer_keeps_the_source()
    {
        var a = New(type: BudgetAdjustmentType.Transfer, competency: "2026-08", sourceCompetency: "2026-07");
        Assert.Equal("2026-07", a.SourceCompetency);
    }

    // ---- State machine: happy paths --------------------------------------------------------------

    [Fact]
    public void Full_lifecycle_triage_to_approved()
    {
        var a = New();
        a.SendToApproval(Actor, At); Assert.Equal(BudgetAdjustmentStatus.AwaitingApproval, a.Status);
        a.Approve(Actor, At, null); Assert.Equal(BudgetAdjustmentStatus.Approved, a.Status);
    }

    [Fact]
    public void Reverse_from_approved()
    {
        var a = Drive(BudgetAdjustmentStatus.Approved);
        a.Reverse(Actor, At, "engano");
        Assert.Equal(BudgetAdjustmentStatus.Reversed, a.Status);
        Assert.Equal("engano", a.Transitions[^1].Reason);
    }

    [Theory]
    [InlineData(BudgetAdjustmentStatus.AwaitingTriage)]
    [InlineData(BudgetAdjustmentStatus.AwaitingApproval)]
    public void Cancel_allowed_before_approval(BudgetAdjustmentStatus from)
    {
        var a = Drive(from);
        a.Cancel(Actor, At);
        Assert.Equal(BudgetAdjustmentStatus.Cancelled, a.Status);
    }

    // ---- State machine: invalid transitions ------------------------------------------------------

    [Fact]
    public void Approve_from_awaiting_triage_is_invalid() => AssertInvalidTransition(() => New().Approve(Actor, At, null));

    [Fact]
    public void SendToApproval_twice_is_invalid()
    {
        var a = Drive(BudgetAdjustmentStatus.AwaitingApproval);
        AssertInvalidTransition(() => a.SendToApproval(Actor, At));
    }

    [Fact]
    public void Reverse_before_approval_is_invalid() =>
        AssertInvalidTransition(() => Drive(BudgetAdjustmentStatus.AwaitingApproval).Reverse(Actor, At));

    [Fact]
    public void Cancel_after_approval_is_invalid() =>
        AssertInvalidTransition(() => Drive(BudgetAdjustmentStatus.Approved).Cancel(Actor, At));

    [Fact]
    public void No_transition_out_of_reversed()
    {
        var a = Drive(BudgetAdjustmentStatus.Reversed);
        AssertInvalidTransition(() => a.Cancel(Actor, At));
        AssertInvalidTransition(() => a.Reverse(Actor, At));
    }

    // ---- Alçada (approval authority limit) -------------------------------------------------------

    [Fact]
    public void Approve_above_limit_is_blocked()
    {
        var a = Drive(BudgetAdjustmentStatus.AwaitingApproval, value: 1000m);
        AssertError(BudgetAdjustmentErrors.ApprovalLimitExceeded, () => a.Approve(Actor, At, approverLimit: 500m));
        Assert.Equal(BudgetAdjustmentStatus.AwaitingApproval, a.Status); // unchanged
    }

    [Fact]
    public void Approve_at_exactly_limit_passes()
    {
        var a = Drive(BudgetAdjustmentStatus.AwaitingApproval, value: 500m);
        a.Approve(Actor, At, approverLimit: 500m);
        Assert.Equal(BudgetAdjustmentStatus.Approved, a.Status);
    }

    [Fact]
    public void Approve_null_limit_is_unlimited()
    {
        var a = Drive(BudgetAdjustmentStatus.AwaitingApproval, value: 1_000_000m);
        a.Approve(Actor, At, approverLimit: null);
        Assert.Equal(BudgetAdjustmentStatus.Approved, a.Status);
    }

    [Fact]
    public void Approve_zero_limit_blocks_any_positive_value()
    {
        var a = Drive(BudgetAdjustmentStatus.AwaitingApproval, value: 1m);
        AssertError(BudgetAdjustmentErrors.ApprovalLimitExceeded, () => a.Approve(Actor, At, approverLimit: 0m));
    }

    [Fact]
    public void Approve_from_wrong_state_is_invalid_transition_before_limit_check()
    {
        // State is validated first: AwaitingTriage with a tiny limit fails as InvalidTransition.
        var a = New(value: 1000m);
        AssertInvalidTransition(() => a.Approve(Actor, At, approverLimit: 1m));
    }

    // ---- StatusChangedAt + transition history ----------------------------------------------------

    [Fact]
    public void MarkCreated_sets_status_changed_at()
    {
        var a = New();
        a.MarkCreated("user-1", At);
        Assert.Equal(At, a.StatusChangedAt);
    }

    [Fact]
    public void Transition_updates_status_changed_at()
    {
        var a = New();
        var later = At.AddDays(1);
        a.SendToApproval(Actor, later);
        Assert.Equal(later, a.StatusChangedAt);
    }

    [Fact]
    public void History_accumulates_in_order()
    {
        var a = New();
        a.SendToApproval(Actor, At);
        a.Approve(Actor, At, null);
        a.Reverse(Actor, At);
        Assert.Equal(
            new[] { BudgetAdjustmentAction.SendToApproval, BudgetAdjustmentAction.Approve, BudgetAdjustmentAction.Reverse },
            a.Transitions.Select(t => t.Action).ToArray());
    }

    [Fact]
    public void Blocked_approval_records_no_history()
    {
        var a = Drive(BudgetAdjustmentStatus.AwaitingApproval, value: 1000m); // 1 transition: SendToApproval
        AssertError(BudgetAdjustmentErrors.ApprovalLimitExceeded, () => a.Approve(Actor, At, approverLimit: 10m));
        Assert.Single(a.Transitions);
    }

    // ---- Update + delete guard -------------------------------------------------------------------

    [Fact]
    public void Update_changes_editable_fields_while_awaiting_triage()
    {
        var a = New();
        a.Update(BudgetAdjustmentType.Transfer, "2026-09", "2026-08", 250m, "nova just", "novas cp");
        Assert.Equal(BudgetAdjustmentType.Transfer, a.Type);
        Assert.Equal("2026-09", a.Competency);
        Assert.Equal("2026-08", a.SourceCompetency);
        Assert.Equal(250m, a.Value);
        Assert.Equal("nova just", a.Justification);
        Assert.Equal("novas cp", a.Counterparts);
    }

    [Fact]
    public void Update_after_send_to_approval_throws_not_editable()
    {
        var a = Drive(BudgetAdjustmentStatus.AwaitingApproval);
        AssertError(BudgetAdjustmentErrors.NotEditable,
            () => a.Update(BudgetAdjustmentType.Supplementation, "2026-07", null, 1m, "j", "c"));
    }

    [Fact]
    public void EnsureDeletable_passes_while_awaiting_triage() => New().EnsureDeletable(); // does not throw

    [Fact]
    public void EnsureDeletable_throws_after_send_to_approval() =>
        AssertError(BudgetAdjustmentErrors.NotDeletable, () => Drive(BudgetAdjustmentStatus.AwaitingApproval).EnsureDeletable());

    // ---- Helpers ---------------------------------------------------------------------------------

    private static void AssertThrowsArgument(string paramName, Action act)
    {
        var ex = Assert.Throws<ArgumentException>(act);
        Assert.Equal(paramName, ex.ParamName);
    }

    private static BudgetAdjustment Drive(BudgetAdjustmentStatus target, decimal value = 100m)
    {
        var a = New(value: value);
        if (target == BudgetAdjustmentStatus.AwaitingTriage) return a;
        a.SendToApproval(Actor, At);
        if (target == BudgetAdjustmentStatus.AwaitingApproval) return a;
        if (target == BudgetAdjustmentStatus.Cancelled) { a.Cancel(Actor, At); return a; }
        a.Approve(Actor, At, null);
        if (target == BudgetAdjustmentStatus.Approved) return a;
        if (target == BudgetAdjustmentStatus.Reversed) { a.Reverse(Actor, At); return a; }
        throw new ArgumentException($"Cannot drive to {target}.", nameof(target));
    }
}
