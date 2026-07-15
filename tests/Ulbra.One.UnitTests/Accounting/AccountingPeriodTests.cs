using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Xunit;

namespace Ulbra.One.UnitTests.Accounting;

public class AccountingPeriodTests
{
    private static readonly Guid Company = Guid.NewGuid();

    [Fact]
    public void New_period_is_open()
    {
        var p = new AccountingPeriod(Company, 2026, 7);
        Assert.Equal(AccountingPeriodStatus.Open, p.Status);
        Assert.False(p.IsClosed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void Ctor_rejects_month_out_of_range(int month)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AccountingPeriod(Company, 2026, month));
    }

    [Fact]
    public void Ctor_rejects_empty_company()
    {
        Assert.Throws<ArgumentException>(() => new AccountingPeriod(Guid.Empty, 2026, 7));
    }

    [Fact]
    public void Close_then_reopen_round_trips()
    {
        var p = new AccountingPeriod(Company, 2026, 7);
        p.Close();
        Assert.True(p.IsClosed);
        p.Reopen();
        Assert.Equal(AccountingPeriodStatus.Open, p.Status);
    }

    [Fact]
    public void Close_is_idempotent()
    {
        var p = new AccountingPeriod(Company, 2026, 7);
        p.Close();
        p.Close();
        Assert.Equal(AccountingPeriodStatus.Closed, p.Status);
    }

    [Fact]
    public void StartClosing_moves_open_to_closing()
    {
        var p = new AccountingPeriod(Company, 2026, 7);
        p.StartClosing();
        Assert.Equal(AccountingPeriodStatus.Closing, p.Status);
    }

    [Fact]
    public void StartClosing_from_closed_is_invalid_transition()
    {
        var p = new AccountingPeriod(Company, 2026, 7);
        p.Close();
        var ex = Assert.Throws<AccountingPeriodException>(() => p.StartClosing());
        Assert.Equal(AccountingPeriodErrors.InvalidTransition.Code, ex.Error.Code);
    }
}
