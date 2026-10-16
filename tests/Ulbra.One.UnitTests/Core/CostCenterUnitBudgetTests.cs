using Ulbra.One.Core.Domain.Entities;
using Xunit;

namespace Ulbra.One.UnitTests.Core;

public class CostCenterUnitBudgetTests
{
    private static CostCenterUnitBudget New(decimal monthlyLimit = 1000m, string competency = "2026-07") =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), competency, monthlyLimit);

    [Fact]
    public void Constructor_sets_fields()
    {
        var cc = Guid.NewGuid();
        var unit = Guid.NewGuid();
        var company = Guid.NewGuid();
        var b = new CostCenterUnitBudget(cc, unit, company, "2026-07", 1234.56m);
        Assert.Equal(cc, b.CostCenterId);
        Assert.Equal(unit, b.UnitId);
        Assert.Equal(company, b.CompanyId);
        Assert.Equal("2026-07", b.Competency);
        Assert.Equal(1234.56m, b.MonthlyLimit);
        Assert.True(b.Active);
    }

    [Fact]
    public void Constructor_rounds_limit_to_two_decimals_away_from_zero() =>
        Assert.Equal(100.13m, New(monthlyLimit: 100.125m).MonthlyLimit);

    [Fact]
    public void Constructor_rejects_negative_limit() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => New(monthlyLimit: -1m));

    [Fact]
    public void Constructor_allows_zero_limit() => Assert.Equal(0m, New(monthlyLimit: 0m).MonthlyLimit);

    [Theory]
    [InlineData("2026-13")]
    [InlineData("202607")]
    [InlineData("")]
    public void Constructor_rejects_invalid_competency(string competency) =>
        Assert.Throws<ArgumentException>(() => New(competency: competency));

    [Fact]
    public void Constructor_rejects_empty_ids()
    {
        Assert.Throws<ArgumentException>(() => new CostCenterUnitBudget(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), "2026-07", 1m));
        Assert.Throws<ArgumentException>(() => new CostCenterUnitBudget(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), "2026-07", 1m));
        Assert.Throws<ArgumentException>(() => new CostCenterUnitBudget(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, "2026-07", 1m));
    }

    [Fact]
    public void Update_changes_limit_and_active()
    {
        var b = New(monthlyLimit: 1000m);
        b.Update(2000m, active: false);
        Assert.Equal(2000m, b.MonthlyLimit);
        Assert.False(b.Active);
    }

    [Fact]
    public void Increase_raises_the_limit()
    {
        var b = New(monthlyLimit: 1000m);
        b.Increase(500m);
        Assert.Equal(1500m, b.MonthlyLimit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Increase_rejects_non_positive_delta(decimal delta) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => New().Increase(delta));

    [Fact]
    public void Decrease_lowers_the_limit()
    {
        var b = New(monthlyLimit: 1000m);
        b.Decrease(300m);
        Assert.Equal(700m, b.MonthlyLimit);
    }

    [Fact]
    public void Decrease_to_exactly_zero_is_allowed()
    {
        var b = New(monthlyLimit: 1000m);
        b.Decrease(1000m);
        Assert.Equal(0m, b.MonthlyLimit);
    }

    [Fact]
    public void Decrease_below_zero_throws()
    {
        var b = New(monthlyLimit: 1000m);
        Assert.Throws<InvalidOperationException>(() => b.Decrease(1001m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Decrease_rejects_non_positive_delta(decimal delta) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => New().Decrease(delta));
}
