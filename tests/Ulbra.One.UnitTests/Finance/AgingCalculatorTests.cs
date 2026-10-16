using Ulbra.One.Finance.Application.Aging;
using Ulbra.One.Finance.Domain;
using Xunit;

namespace Ulbra.One.UnitTests.Finance;

public class AgingCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 6, 15);

    [Theory]
    [InlineData("2026-06-15", AgingBucket.NotYetDue)]    // due today → not yet due
    [InlineData("2026-06-20", AgingBucket.NotYetDue)]    // future
    [InlineData("2026-06-14", AgingBucket.Overdue1To30)] // 1 day overdue
    [InlineData("2026-05-16", AgingBucket.Overdue1To30)] // 30 days overdue
    [InlineData("2026-05-15", AgingBucket.Overdue31To60)]// 31 days
    [InlineData("2026-04-16", AgingBucket.Overdue31To60)]// 60 days
    [InlineData("2026-04-15", AgingBucket.Overdue61To90)]// 61 days
    [InlineData("2026-03-17", AgingBucket.Overdue61To90)]// 90 days
    [InlineData("2026-03-16", AgingBucket.Overdue90Plus)]// 91 days
    [InlineData("2025-06-15", AgingBucket.Overdue90Plus)]// way overdue
    public void BucketOf_classifies_by_days_overdue(string due, AgingBucket expected)
    {
        Assert.Equal(expected, AgingCalculator.BucketOf(DateOnly.Parse(due), Today));
    }

    [Fact]
    public void DaysOverdue_is_zero_when_due_today_or_future()
    {
        Assert.Equal(0, AgingCalculator.DaysOverdue(new DateOnly(2026, 6, 15), Today));
        Assert.Equal(0, AgingCalculator.DaysOverdue(new DateOnly(2026, 7, 1), Today));
    }

    [Fact]
    public void DaysOverdue_counts_past_days()
    {
        Assert.Equal(5, AgingCalculator.DaysOverdue(new DateOnly(2026, 6, 10), Today));
    }
}
