using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Accounting.Domain;
using Xunit;

namespace Ulbra.One.UnitTests.Accounting;

public class BalanceSheetCalculatorTests
{
    private static readonly Guid Cash = Guid.NewGuid();
    private static readonly Guid Loan = Guid.NewGuid();
    private static readonly Guid Capital = Guid.NewGuid();
    private static readonly Guid Sales = Guid.NewGuid();
    private static readonly Guid Expense = Guid.NewGuid();

    private static readonly DateOnly AsOf = new(2026, 6, 30);

    private static PostedLine Line(Guid acct, string code, AccountGroup group, EntrySide side, decimal amount, DateOnly? date = null)
    {
        var nb = group is AccountGroup.Asset or AccountGroup.Expense ? NormalBalance.Debit : NormalBalance.Credit;
        return new PostedLine(acct, code, code, group, nb, side, amount, date ?? new DateOnly(2026, 6, 1));
    }

    [Fact]
    public void Empty_input_balances_at_zero()
    {
        var bp = BalanceSheetCalculator.Build([], AsOf);
        Assert.Equal(0m, bp.TotalAssets);
        Assert.Equal(0m, bp.PeriodResult);
        Assert.True(bp.IsBalanced);
    }

    [Fact]
    public void Identity_holds_with_period_result_in_equity()
    {
        // Opening: Cash 1000 debit / Capital 1000 credit. Loan 400 credit / Cash +400 debit.
        // Sale: Cash +500 debit / Sales 500 credit. Expense 200 debit / Cash -200 credit.
        var lines = new[]
        {
            Line(Cash, "1.1", AccountGroup.Asset, EntrySide.Debit, 1000m),
            Line(Capital, "5.1", AccountGroup.Equity, EntrySide.Credit, 1000m),
            Line(Cash, "1.1", AccountGroup.Asset, EntrySide.Debit, 400m),
            Line(Loan, "2.1", AccountGroup.Liability, EntrySide.Credit, 400m),
            Line(Cash, "1.1", AccountGroup.Asset, EntrySide.Debit, 500m),
            Line(Sales, "3.1", AccountGroup.Revenue, EntrySide.Credit, 500m),
            Line(Expense, "4.1", AccountGroup.Expense, EntrySide.Debit, 200m),
            Line(Cash, "1.1", AccountGroup.Asset, EntrySide.Credit, 200m),
        };

        var bp = BalanceSheetCalculator.Build(lines, AsOf);

        Assert.Equal(1700m, bp.TotalAssets);       // 1000 + 400 + 500 - 200
        Assert.Equal(400m, bp.TotalLiabilities);
        Assert.Equal(300m, bp.PeriodResult);        // revenue 500 - expense 200
        Assert.Equal(1300m, bp.TotalEquity);        // capital 1000 + result 300
        Assert.True(bp.IsBalanced);                 // 1700 == 400 + 1300
    }

    [Fact]
    public void Period_result_appears_as_explicit_equity_line()
    {
        var lines = new[]
        {
            Line(Cash, "1.1", AccountGroup.Asset, EntrySide.Debit, 500m),
            Line(Sales, "3.1", AccountGroup.Revenue, EntrySide.Credit, 500m),
        };
        var bp = BalanceSheetCalculator.Build(lines, AsOf);
        var resultLine = Assert.Single(bp.Equity, l => l.AccountCode == BalanceSheetCalculator.PeriodResultCode);
        Assert.Equal(500m, resultLine.Balance);
        Assert.True(bp.IsBalanced);
    }

    [Fact]
    public void Excludes_lines_after_cutoff()
    {
        var lines = new[]
        {
            Line(Cash, "1.1", AccountGroup.Asset, EntrySide.Debit, 500m, new DateOnly(2026, 6, 1)),
            Line(Capital, "5.1", AccountGroup.Equity, EntrySide.Credit, 500m, new DateOnly(2026, 6, 1)),
            Line(Cash, "1.1", AccountGroup.Asset, EntrySide.Debit, 999m, new DateOnly(2026, 7, 1)),
            Line(Capital, "5.1", AccountGroup.Equity, EntrySide.Credit, 999m, new DateOnly(2026, 7, 1)),
        };
        var bp = BalanceSheetCalculator.Build(lines, AsOf);
        Assert.Equal(500m, bp.TotalAssets);
        Assert.True(bp.IsBalanced);
    }

    [Fact]
    public void Loss_yields_negative_period_result_and_still_balances()
    {
        var lines = new[]
        {
            Line(Cash, "1.1", AccountGroup.Asset, EntrySide.Debit, 1000m),
            Line(Capital, "5.1", AccountGroup.Equity, EntrySide.Credit, 1000m),
            Line(Expense, "4.1", AccountGroup.Expense, EntrySide.Debit, 300m),
            Line(Cash, "1.1", AccountGroup.Asset, EntrySide.Credit, 300m),
        };
        var bp = BalanceSheetCalculator.Build(lines, AsOf);
        Assert.Equal(700m, bp.TotalAssets);
        Assert.Equal(-300m, bp.PeriodResult);
        Assert.Equal(700m, bp.TotalEquity); // 1000 - 300
        Assert.True(bp.IsBalanced);
    }
}
