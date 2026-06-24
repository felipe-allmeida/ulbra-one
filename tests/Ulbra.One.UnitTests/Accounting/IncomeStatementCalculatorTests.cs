using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Accounting.Domain;
using Xunit;

namespace Ulbra.One.UnitTests.Accounting;

public class IncomeStatementCalculatorTests
{
    private static readonly Guid Sales = Guid.NewGuid();
    private static readonly Guid Rent = Guid.NewGuid();
    private static readonly Guid Salaries = Guid.NewGuid();
    private static readonly Guid Cash = Guid.NewGuid();

    private static readonly DateOnly From = new(2026, 6, 1);
    private static readonly DateOnly To = new(2026, 6, 30);

    private static PostedLine Line(Guid acct, string code, AccountGroup group, EntrySide side, decimal amount, DateOnly? date = null)
    {
        var nb = LedgerAccountDefault(group);
        return new PostedLine(acct, code, code, group, nb, side, amount, date ?? From);
    }

    private static NormalBalance LedgerAccountDefault(AccountGroup g) =>
        g is AccountGroup.Asset or AccountGroup.Expense ? NormalBalance.Debit : NormalBalance.Credit;

    [Fact]
    public void Empty_period_yields_zero_result()
    {
        var dre = IncomeStatementCalculator.Build([], From, To);
        Assert.Empty(dre.Revenues);
        Assert.Empty(dre.Expenses);
        Assert.Equal(0m, dre.Result);
    }

    [Fact]
    public void Computes_result_as_revenue_minus_expense()
    {
        var lines = new[]
        {
            Line(Sales, "3.1", AccountGroup.Revenue, EntrySide.Credit, 1000m),
            Line(Rent, "4.1", AccountGroup.Expense, EntrySide.Debit, 300m),
            Line(Salaries, "4.2", AccountGroup.Expense, EntrySide.Debit, 200m),
            // Asset line is ignored by the DRE.
            Line(Cash, "1.1", AccountGroup.Asset, EntrySide.Debit, 1000m),
        };

        var dre = IncomeStatementCalculator.Build(lines, From, To);

        Assert.Equal(1000m, dre.TotalRevenue);
        Assert.Equal(500m, dre.TotalExpense);
        Assert.Equal(500m, dre.Result);
        Assert.Single(dre.Revenues);
        Assert.Equal(2, dre.Expenses.Count);
    }

    [Fact]
    public void Excludes_lines_outside_the_competence_range()
    {
        var lines = new[]
        {
            Line(Sales, "3.1", AccountGroup.Revenue, EntrySide.Credit, 1000m, new DateOnly(2026, 6, 15)),
            Line(Sales, "3.1", AccountGroup.Revenue, EntrySide.Credit, 999m, new DateOnly(2026, 7, 1)),
        };

        var dre = IncomeStatementCalculator.Build(lines, From, To);

        Assert.Equal(1000m, dre.TotalRevenue);
    }

    [Fact]
    public void Negative_result_for_loss()
    {
        var lines = new[]
        {
            Line(Sales, "3.1", AccountGroup.Revenue, EntrySide.Credit, 100m),
            Line(Rent, "4.1", AccountGroup.Expense, EntrySide.Debit, 400m),
        };
        var dre = IncomeStatementCalculator.Build(lines, From, To);
        Assert.Equal(-300m, dre.Result);
    }

    [Fact]
    public void Rejects_inverted_range()
    {
        Assert.Throws<ArgumentException>(() => IncomeStatementCalculator.Build([], To, From));
    }
}
