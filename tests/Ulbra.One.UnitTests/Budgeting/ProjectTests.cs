using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;
using Xunit;

namespace Ulbra.One.UnitTests.Budgeting;

public class ProjectTests
{
    private static Project New(decimal budgetLimit = 100_000m, decimal initialConsumed = 0m) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Obra Bloco A", budgetLimit, initialConsumed);

    [Fact]
    public void Previsto_sums_all_pipeline_but_saldo_only_counts_committed()
    {
        var p = New(budgetLimit: 100_000m);

        // 50k in pipeline (forecast), but only 20k reached payment -> saldo drops only the 20k (AEL-844).
        var m = p.CalculateMetrics(requisitionCosts: [30_000m, 20_000m], committedCosts: [20_000m], paidAmounts: []);

        Assert.Equal(50_000m, m.Previsto);
        Assert.Equal(80_000m, m.Saldo);
    }

    [Fact]
    public void Realizado_sums_paid_amounts()
    {
        var m = New().CalculateMetrics(requisitionCosts: [30_000m], committedCosts: [], paidAmounts: [8_000m, 2_000m]);

        Assert.Equal(10_000m, m.Realizado);
    }

    [Fact]
    public void InitialConsumedValue_counts_toward_previsto_realizado_and_saldo()
    {
        var p = New(budgetLimit: 100_000m, initialConsumed: 10_000m);

        var m = p.CalculateMetrics(requisitionCosts: [20_000m], committedCosts: [20_000m], paidAmounts: [5_000m]);

        Assert.Equal(30_000m, m.Previsto);
        Assert.Equal(15_000m, m.Realizado);
        Assert.Equal(70_000m, m.Saldo); // 100k - (20k committed-to-payment + 10k initial)
    }

    [Fact]
    public void Pipeline_over_ceiling_does_NOT_flag_overrun_until_it_reaches_payment()
    {
        // Forecast exceeds the ceiling, but nothing reached payment -> saldo intact, no overrun (AEL-844).
        var m = New(budgetLimit: 10_000m).CalculateMetrics(requisitionCosts: [12_000m], committedCosts: [], paidAmounts: []);

        Assert.Equal(12_000m, m.Previsto);
        Assert.Equal(10_000m, m.Saldo);
        Assert.False(m.IsOverCeiling);
    }

    [Fact]
    public void Committed_over_ceiling_yields_negative_saldo_and_flags_overrun()
    {
        var m = New(budgetLimit: 10_000m).CalculateMetrics(requisitionCosts: [12_000m], committedCosts: [12_000m], paidAmounts: []);

        Assert.Equal(-2_000m, m.Saldo);
        Assert.True(m.IsOverCeiling);
    }

    [Fact]
    public void Saldo_within_ceiling_does_not_flag_overrun() =>
        Assert.False(New(budgetLimit: 10_000m).CalculateMetrics([10_000m], [10_000m], []).IsOverCeiling);

    [Fact]
    public void Percentages_are_relative_to_the_ceiling()
    {
        var m = New(budgetLimit: 100_000m).CalculateMetrics(requisitionCosts: [25_000m], committedCosts: [], paidAmounts: [10_000m]);

        Assert.Equal(25m, m.PercentPrevisto);
        Assert.Equal(10m, m.PercentRealizado);
    }

    [Fact]
    public void Zero_ceiling_yields_zero_percentages_instead_of_dividing_by_zero()
    {
        var m = New(budgetLimit: 0m).CalculateMetrics(requisitionCosts: [100m], committedCosts: [], paidAmounts: [50m]);

        Assert.Equal(0m, m.PercentPrevisto);
        Assert.Equal(0m, m.PercentRealizado);
    }

    // ---- Guards -------------------------------------------------------------------------------

    [Fact]
    public void Empty_ids_throw()
    {
        Assert.Throws<ArgumentException>(() => new Project(Guid.Empty, Guid.NewGuid(), "P", 1m));
        Assert.Throws<ArgumentException>(() => new Project(Guid.NewGuid(), Guid.Empty, "P", 1m));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_name_throws(string name) =>
        Assert.Throws<ArgumentException>(() => new Project(Guid.NewGuid(), Guid.NewGuid(), name, 1m));

    [Fact]
    public void Name_is_trimmed() =>
        Assert.Equal("Obra Bloco A", new Project(Guid.NewGuid(), Guid.NewGuid(), "  Obra Bloco A  ", 1m).Name);

    [Fact]
    public void Negative_budget_limit_throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => New(budgetLimit: -1m));

    [Fact]
    public void Negative_initial_consumed_throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => New(initialConsumed: -1m));

    [Fact]
    public void Amounts_are_rounded_to_two_decimals()
    {
        var p = New(budgetLimit: 100.125m, initialConsumed: 10.567m);

        Assert.Equal(100.13m, p.BudgetLimit);
        Assert.Equal(10.57m, p.InitialConsumedValue);
    }

    // ---- Status -------------------------------------------------------------------------------

    [Fact]
    public void Starts_active() => Assert.Equal(ProjectStatus.Active, New().Status);

    [Fact]
    public void Finish_marks_finished()
    {
        var p = New();
        p.Finish();
        Assert.Equal(ProjectStatus.Finished, p.Status);
    }

    [Fact]
    public void Cancel_marks_cancelled()
    {
        var p = New();
        p.Cancel();
        Assert.Equal(ProjectStatus.Cancelled, p.Status);
    }

    [Fact]
    public void Finishing_a_cancelled_project_throws()
    {
        var p = New();
        p.Cancel();
        Assert.Throws<InvalidOperationException>(() => p.Finish());
    }

    // ---- Update / audit -----------------------------------------------------------------------

    [Fact]
    public void Update_changes_name_and_ceiling()
    {
        var p = New();

        p.Update("Obra Bloco B", 250_000m, 5_000m);

        Assert.Equal("Obra Bloco B", p.Name);
        Assert.Equal(250_000m, p.BudgetLimit);
        Assert.Equal(5_000m, p.InitialConsumedValue);
    }

    [Fact]
    public void MarkCreated_sets_audit_fields()
    {
        var p = New();
        var at = DateTimeOffset.UtcNow;

        p.MarkCreated("user-1", at);

        Assert.Equal("user-1", p.CreatedBy);
        Assert.Equal(at, p.CreatedAt);
        Assert.Equal("user-1", p.UpdatedBy);
        Assert.Equal(at, p.UpdatedAt);
    }
}
