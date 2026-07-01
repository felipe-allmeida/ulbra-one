using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Xunit;

namespace Ulbra.One.UnitTests.Finance;

public class InstallmentHoldTests
{
    private static Installment OpenInstallment() => new(1, new DateOnly(2026, 7, 5), 100m);

    [Fact]
    public void Hold_sets_flag_and_reason()
    {
        var i = OpenInstallment();
        i.Hold("under dispute");
        Assert.True(i.IsHeld);
        Assert.Equal("under dispute", i.HoldReason);
    }

    [Fact]
    public void ReleaseHold_clears_flag_and_reason()
    {
        var i = OpenInstallment();
        i.Hold("x");
        i.ReleaseHold();
        Assert.False(i.IsHeld);
        Assert.Null(i.HoldReason);
    }

    [Fact]
    public void Hold_requires_open_status()
    {
        var i = OpenInstallment();
        i.MarkInRemittance(); // now InRemittance
        Assert.Throws<InvalidOperationException>(() => i.Hold("too late"));
    }

    [Fact]
    public void SetScheduledPaymentDate_updates_the_date()
    {
        var i = OpenInstallment();
        i.SetScheduledPaymentDate(new DateOnly(2026, 7, 15));
        Assert.Equal(new DateOnly(2026, 7, 15), i.ScheduledPaymentDate);
    }
}
