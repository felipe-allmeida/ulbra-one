using Ulbra.One.Registry.Domain.ValueObjects;
using Xunit;

namespace Ulbra.One.UnitTests.Registry;

public class PaymentScheduleTests
{
    [Fact]
    public void OnDueDate_returns_the_due_date()
    {
        var s = PaymentSchedule.Default;
        Assert.Equal(PaymentScheduleMode.OnDueDate, s.Mode);
        Assert.Equal(new DateOnly(2026, 7, 5), s.Resolve(new DateOnly(2026, 7, 5)));
    }

    [Fact]
    public void FixedDayOfMonth_uses_that_day_when_on_or_after_due()
    {
        var s = new PaymentSchedule(PaymentScheduleMode.FixedDayOfMonth, 15);
        Assert.Equal(new DateOnly(2026, 7, 15), s.Resolve(new DateOnly(2026, 7, 5)));
    }

    [Fact]
    public void FixedDayOfMonth_rolls_forward_when_day_already_passed()
    {
        var s = new PaymentSchedule(PaymentScheduleMode.FixedDayOfMonth, 10);
        // due on the 20th → the 10th already passed this month → next month's 10th
        Assert.Equal(new DateOnly(2026, 8, 10), s.Resolve(new DateOnly(2026, 7, 20)));
    }

    [Fact]
    public void FixedDayOfMonth_clamps_to_month_end()
    {
        var s = new PaymentSchedule(PaymentScheduleMode.FixedDayOfMonth, 31);
        Assert.Equal(new DateOnly(2026, 2, 28), s.Resolve(new DateOnly(2026, 2, 3)));
    }

    [Fact]
    public void FirstDayOfMonth_returns_first_of_month_on_or_after_due()
    {
        var s = new PaymentSchedule(PaymentScheduleMode.FirstDayOfMonth, null);
        // due on the 5th → first of NEXT month (first-of-this-month already passed)
        Assert.Equal(new DateOnly(2026, 8, 1), s.Resolve(new DateOnly(2026, 7, 5)));
    }
}
