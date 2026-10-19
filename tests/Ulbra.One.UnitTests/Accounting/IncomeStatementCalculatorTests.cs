using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Accounting.Domain;
using Xunit;

namespace Ulbra.One.UnitTests.Accounting;

public class IncomeStatementCalculatorTests
{
    private static readonly DateOnly From = new(2026, 6, 1);
    private static readonly DateOnly To = new(2026, 6, 30);

    /// <summary>One posted line to a fresh account, tagged with its DRE line. Group only decides that the line
    /// is a result line; the calculator signs contributions as credit − debit regardless of NormalBalance.</summary>
    private static PostedLine Line(string code, AccountGroup group, DreLine? dre, EntrySide side, decimal amount, DateOnly? date = null)
    {
        var nb = group is AccountGroup.Asset or AccountGroup.Expense ? NormalBalance.Debit : NormalBalance.Credit;
        return new PostedLine(Guid.NewGuid(), code, code, group, nb, side, amount, date ?? From, DreLine: dre);
    }

    [Fact]
    public void Empty_period_yields_zero_cascade()
    {
        var dre = IncomeStatementCalculator.Build([], From, To);
        Assert.Empty(dre.Sections);
        Assert.Equal(0m, dre.NetRevenue);
        Assert.Equal(0m, dre.GrossProfit);
        Assert.Equal(0m, dre.NetIncome);
    }

    [Fact]
    public void Builds_the_full_cascade_with_running_subtotals()
    {
        var lines = new[]
        {
            Line("3.2.05.01.001", AccountGroup.Revenue, DreLine.GrossRevenue, EntrySide.Credit, 10000m),
            Line("3.2.10.01.001", AccountGroup.Revenue, DreLine.RevenueDeductions, EntrySide.Debit, 1500m),
            Line("5.2.10.01.001", AccountGroup.Expense, DreLine.CostOfSales, EntrySide.Debit, 3000m),
            Line("3.2.15.01.001", AccountGroup.Revenue, DreLine.OtherOperatingIncome, EntrySide.Credit, 500m),
            Line("4.2.15.01.001", AccountGroup.Expense, DreLine.OperatingExpenses, EntrySide.Debit, 2000m),
            Line("3.2.25.01.001", AccountGroup.Revenue, DreLine.FinancialRevenue, EntrySide.Credit, 200m),
            Line("4.2.30.01.001", AccountGroup.Expense, DreLine.FinancialExpense, EntrySide.Debit, 700m),
            Line("4.2.35.05.040", AccountGroup.Expense, DreLine.IncomeTax, EntrySide.Debit, 400m),
            Line("4.2.35.05.020", AccountGroup.Expense, DreLine.SocialContribution, EntrySide.Debit, 150m),
        };

        var dre = IncomeStatementCalculator.Build(lines, From, To);

        Assert.Equal(10000m, dre.GrossRevenue);
        Assert.Equal(-1500m, dre.RevenueDeductions);
        Assert.Equal(8500m, dre.NetRevenue);
        Assert.Equal(-3000m, dre.CostOfSales);
        Assert.Equal(5500m, dre.GrossProfit);
        Assert.Equal(500m, dre.OtherOperatingIncome);
        Assert.Equal(-2000m, dre.OperatingExpenses);
        Assert.Equal(4000m, dre.OperatingResult);
        Assert.Equal(200m, dre.FinancialRevenue);
        Assert.Equal(-700m, dre.FinancialExpense);
        Assert.Equal(-500m, dre.FinancialResult);
        Assert.Equal(3500m, dre.PreTaxResult);
        Assert.Equal(-400m, dre.IncomeTax);
        Assert.Equal(-150m, dre.SocialContribution);
        Assert.Equal(2950m, dre.NetIncome);
    }

    [Fact]
    public void Sections_are_ordered_down_the_cascade_and_group_their_accounts()
    {
        var lines = new[]
        {
            Line("4.2.15.05.020", AccountGroup.Expense, DreLine.OperatingExpenses, EntrySide.Debit, 200m),
            Line("4.2.15.05.040", AccountGroup.Expense, DreLine.OperatingExpenses, EntrySide.Debit, 300m),
            Line("3.2.05.01.001", AccountGroup.Revenue, DreLine.GrossRevenue, EntrySide.Credit, 900m),
        };

        var dre = IncomeStatementCalculator.Build(lines, From, To);

        Assert.Equal([DreLine.GrossRevenue, DreLine.OperatingExpenses], dre.Sections.Select(s => s.Line));
        var opex = dre.Sections.Single(s => s.Line == DreLine.OperatingExpenses);
        Assert.Equal(2, opex.Lines.Count);
        Assert.Equal(-500m, opex.Total); // both debits contribute negatively
    }

    [Fact]
    public void Revenue_deductions_reduce_net_revenue()
    {
        var lines = new[]
        {
            Line("3.2.05.01.001", AccountGroup.Revenue, DreLine.GrossRevenue, EntrySide.Credit, 1000m),
            Line("3.2.10.01.001", AccountGroup.Revenue, DreLine.RevenueDeductions, EntrySide.Debit, 200m),
        };
        var dre = IncomeStatementCalculator.Build(lines, From, To);
        Assert.Equal(800m, dre.NetRevenue);
    }

    [Fact]
    public void Excludes_lines_outside_the_competence_range()
    {
        var lines = new[]
        {
            Line("3.2.05.01.001", AccountGroup.Revenue, DreLine.GrossRevenue, EntrySide.Credit, 1000m, new DateOnly(2026, 6, 15)),
            Line("3.2.05.01.002", AccountGroup.Revenue, DreLine.GrossRevenue, EntrySide.Credit, 999m, new DateOnly(2026, 7, 1)),
        };
        var dre = IncomeStatementCalculator.Build(lines, From, To);
        Assert.Equal(1000m, dre.GrossRevenue);
    }

    [Fact]
    public void Unclassified_result_accounts_fall_back_to_an_operating_bucket()
    {
        var lines = new[]
        {
            Line("3.9.99.99.999", AccountGroup.Revenue, null, EntrySide.Credit, 100m),
            Line("4.9.99.99.999", AccountGroup.Expense, null, EntrySide.Debit, 40m),
        };
        var dre = IncomeStatementCalculator.Build(lines, From, To);
        Assert.Equal(100m, dre.OtherOperatingIncome);
        Assert.Equal(-40m, dre.OperatingExpenses);
        Assert.Equal(60m, dre.NetIncome);
    }

    [Fact]
    public void Excludes_year_end_closing_entries()
    {
        var acct = Guid.NewGuid();
        var lines = new[]
        {
            // Operational revenue.
            new PostedLine(acct, "3.2.05", "Receita", AccountGroup.Revenue, NormalBalance.Credit,
                EntrySide.Credit, 1000m, From, DreLine: DreLine.GrossRevenue),
            // The year-end closing entry debits the same account to zero it — must NOT collapse the DRE.
            new PostedLine(acct, "3.2.05", "Receita", AccountGroup.Revenue, NormalBalance.Credit,
                EntrySide.Debit, 1000m, From, DreLine: DreLine.GrossRevenue, Origin: JournalOrigin.Closing),
        };
        var dre = IncomeStatementCalculator.Build(lines, From, To);
        Assert.Equal(1000m, dre.GrossRevenue);
        Assert.Equal(1000m, dre.NetIncome);
    }

    [Fact]
    public void Rejects_inverted_range()
    {
        Assert.Throws<ArgumentException>(() => IncomeStatementCalculator.Build([], To, From));
    }
}
