using Ulbra.One.Accounting.Domain;

namespace Ulbra.One.Accounting.Application.Reports;

/// <summary>An account line of the DRE. <see cref="Amount"/> is the account's contribution to the result
/// (credit − debit): positive for revenues, negative for costs/expenses/deductions — so lines and subtotals
/// are simply additive down the cascade.</summary>
public sealed record IncomeStatementLine(
    Guid LedgerAccountId,
    string AccountCode,
    string AccountName,
    DreLine DreLine,
    decimal Amount);

/// <summary>One line of the DRE cascade: the accounts that roll up to a <see cref="DreLine"/> and their
/// combined contribution to the result (credit − debit).</summary>
public sealed record IncomeStatementSection(
    DreLine Line,
    decimal Total,
    IReadOnlyList<IncomeStatementLine> Lines);

/// <summary>The structured income statement (DRE) for a competence range: the per-<see cref="DreLine"/> sections
/// plus the derived cascade subtotals. All values are signed as contributions to the result (positive = lucro),
/// so every subtotal is a running sum of the ones above it.</summary>
public sealed record IncomeStatement(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<IncomeStatementSection> Sections,
    decimal GrossRevenue,
    decimal RevenueDeductions,
    decimal NetRevenue,
    decimal CostOfSales,
    decimal GrossProfit,
    decimal OtherOperatingIncome,
    decimal OperatingExpenses,
    decimal OperatingResult,
    decimal FinancialRevenue,
    decimal FinancialExpense,
    decimal FinancialResult,
    decimal OtherResult,
    decimal PreTaxResult,
    decimal IncomeTax,
    decimal SocialContribution,
    decimal NetIncome);

/// <summary>Pure calculator that folds posted lines into the structured DRE cascade over a competence range:
/// gross revenue → (−)deductions → net revenue → (−)COGS → gross profit → (+)other operating income →
/// (−)operating expenses → operating result → (±)financial result → (±)other → pre-tax result (LAIR) →
/// (−)IRPJ → (−)CSLL → net income. Each result account contributes credit − debit (revenue +, expense −), and
/// the account's <see cref="DreLine"/> places it in the cascade. No EF, no I/O.</summary>
public static class IncomeStatementCalculator
{
    public static IncomeStatement Build(IEnumerable<PostedLine> lines, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (to < from) throw new ArgumentException("'to' must not precede 'from'.", nameof(to));

        var inRange = lines.Where(l =>
            l.CompetenceDate >= from && l.CompetenceDate <= to &&
            l.AccountGroup is AccountGroup.Revenue or AccountGroup.Expense &&
            // Exclude the year-end closing entries (they zero the result accounts) so the DRE keeps showing the
            // operating result — which equals the amount transferred to Lucros/Prejuízos Acumulados.
            l.Origin != JournalOrigin.Closing);

        var accountLines = inRange
            .GroupBy(l => l.LedgerAccountId)
            .Select(g =>
            {
                var first = g.First();
                // Contribution to the result: credit − debit for every account (revenue +, expense/deduction −).
                var amount = g.Where(l => l.Side == EntrySide.Credit).Sum(l => l.Amount)
                           - g.Where(l => l.Side == EntrySide.Debit).Sum(l => l.Amount);
                var dreLine = first.DreLine ?? FallbackLine(first.AccountGroup);
                return new IncomeStatementLine(first.LedgerAccountId, first.AccountCode, first.AccountName, dreLine, amount);
            })
            .ToList();

        var sections = accountLines
            .GroupBy(l => l.DreLine)
            .Select(g => new IncomeStatementSection(
                g.Key,
                g.Sum(l => l.Amount),
                g.OrderBy(l => l.AccountCode, StringComparer.Ordinal).ToList()))
            .OrderBy(s => (int)s.Line)
            .ToList();

        decimal Total(DreLine line) => sections.FirstOrDefault(s => s.Line == line)?.Total ?? 0m;

        var grossRevenue = Total(DreLine.GrossRevenue);
        var revenueDeductions = Total(DreLine.RevenueDeductions);
        var netRevenue = grossRevenue + revenueDeductions;
        var costOfSales = Total(DreLine.CostOfSales);
        var grossProfit = netRevenue + costOfSales;
        var otherOperatingIncome = Total(DreLine.OtherOperatingIncome);
        var operatingExpenses = Total(DreLine.OperatingExpenses);
        var operatingResult = grossProfit + otherOperatingIncome + operatingExpenses;
        var financialRevenue = Total(DreLine.FinancialRevenue);
        var financialExpense = Total(DreLine.FinancialExpense);
        var financialResult = financialRevenue + financialExpense;
        var otherResult = Total(DreLine.Other);
        var preTaxResult = operatingResult + financialResult + otherResult;
        var incomeTax = Total(DreLine.IncomeTax);
        var socialContribution = Total(DreLine.SocialContribution);
        var netIncome = preTaxResult + incomeTax + socialContribution;

        return new IncomeStatement(from, to, sections,
            grossRevenue, revenueDeductions, netRevenue, costOfSales, grossProfit,
            otherOperatingIncome, operatingExpenses, operatingResult,
            financialRevenue, financialExpense, financialResult, otherResult,
            preTaxResult, incomeTax, socialContribution, netIncome);
    }

    /// <summary>Bucket for a result account whose <see cref="DreLine"/> was never classified (defensive: the seed
    /// and migration backfill classify every 3/4/5 account). Keeps it in the cascade rather than dropping it.</summary>
    private static DreLine FallbackLine(AccountGroup group) =>
        group == AccountGroup.Revenue ? DreLine.OtherOperatingIncome : DreLine.OperatingExpenses;
}
