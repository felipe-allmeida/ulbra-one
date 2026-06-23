using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Accounting.Domain;
using Xunit;

namespace Ulbra.One.UnitTests.Accounting;

public class TrialBalanceCalculatorTests
{
    private static readonly Guid Cash = Guid.NewGuid();
    private static readonly Guid Revenue = Guid.NewGuid();
    private static readonly DateOnly D = new(2026, 6, 1);

    private static PostedLine Line(Guid acct, string code, AccountGroup group, NormalBalance nb, EntrySide side, decimal amount) =>
        new(acct, code, code, group, nb, side, amount, D);

    [Fact]
    public void Empty_input_yields_balanced_empty_report()
    {
        var tb = TrialBalanceCalculator.Build([]);
        Assert.Empty(tb.Rows);
        Assert.Equal(0m, tb.TotalDebit);
        Assert.Equal(0m, tb.TotalCredit);
        Assert.True(tb.IsBalanced);
    }

    [Fact]
    public void Aggregates_per_account_and_respects_normal_balance()
    {
        var lines = new[]
        {
            Line(Cash, "1.1", AccountGroup.Asset, NormalBalance.Debit, EntrySide.Debit, 300m),
            Line(Cash, "1.1", AccountGroup.Asset, NormalBalance.Debit, EntrySide.Credit, 100m),
            Line(Revenue, "3.1", AccountGroup.Revenue, NormalBalance.Credit, EntrySide.Credit, 200m),
        };

        var tb = TrialBalanceCalculator.Build(lines);

        var cash = Assert.Single(tb.Rows, r => r.LedgerAccountId == Cash);
        Assert.Equal(300m, cash.TotalDebit);
        Assert.Equal(100m, cash.TotalCredit);
        Assert.Equal(200m, cash.Balance); // debit-normal: 300 - 100

        var rev = Assert.Single(tb.Rows, r => r.LedgerAccountId == Revenue);
        Assert.Equal(200m, rev.Balance); // credit-normal: 200 - 0
    }

    [Fact]
    public void Grand_totals_balance_for_double_entry_input()
    {
        var lines = new[]
        {
            Line(Cash, "1.1", AccountGroup.Asset, NormalBalance.Debit, EntrySide.Debit, 500m),
            Line(Revenue, "3.1", AccountGroup.Revenue, NormalBalance.Credit, EntrySide.Credit, 500m),
        };

        var tb = TrialBalanceCalculator.Build(lines);

        Assert.Equal(500m, tb.TotalDebit);
        Assert.Equal(500m, tb.TotalCredit);
        Assert.True(tb.IsBalanced);
    }

    [Fact]
    public void Rows_ordered_by_account_code()
    {
        var lines = new[]
        {
            Line(Revenue, "3.1", AccountGroup.Revenue, NormalBalance.Credit, EntrySide.Credit, 10m),
            Line(Cash, "1.1", AccountGroup.Asset, NormalBalance.Debit, EntrySide.Debit, 10m),
        };
        var tb = TrialBalanceCalculator.Build(lines);
        Assert.Equal("1.1", tb.Rows[0].AccountCode);
        Assert.Equal("3.1", tb.Rows[1].AccountCode);
    }
}
