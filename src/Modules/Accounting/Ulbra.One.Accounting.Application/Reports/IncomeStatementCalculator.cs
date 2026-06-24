using Ulbra.One.Accounting.Domain;

namespace Ulbra.One.Accounting.Application.Reports;

/// <summary>An account line in the income statement (DRE), with its net amount over the period
/// (revenue positive, expense positive — sign carried by <see cref="AccountGroup"/>).</summary>
public sealed record IncomeStatementLine(
    Guid LedgerAccountId,
    string AccountCode,
    string AccountName,
    AccountGroup AccountGroup,
    decimal Amount);

/// <summary>The income statement (DRE) for a competence range: revenue and expense lines plus the result.</summary>
public sealed record IncomeStatement(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<IncomeStatementLine> Revenues,
    IReadOnlyList<IncomeStatementLine> Expenses,
    decimal TotalRevenue,
    decimal TotalExpense)
{
    /// <summary>Resultado do exercício: revenue minus expense (positive = lucro, negative = prejuízo).</summary>
    public decimal Result => TotalRevenue - TotalExpense;
}

/// <summary>Pure calculator that produces a DRE from posted lines within a competence range. No EF, no I/O.</summary>
public static class IncomeStatementCalculator
{
    public static IncomeStatement Build(IEnumerable<PostedLine> lines, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (to < from) throw new ArgumentException("'to' must not precede 'from'.", nameof(to));

        var inRange = lines.Where(l =>
            l.CompetenceDate >= from && l.CompetenceDate <= to &&
            l.AccountGroup is AccountGroup.Revenue or AccountGroup.Expense);

        var byAccount = inRange
            .GroupBy(l => l.LedgerAccountId)
            .Select(g =>
            {
                var first = g.First();
                var debit = g.Where(l => l.Side == EntrySide.Debit).Sum(l => l.Amount);
                var credit = g.Where(l => l.Side == EntrySide.Credit).Sum(l => l.Amount);
                // Revenue is naturally credit, expense naturally debit; carry the account's own amount.
                var amount = first.AccountGroup == AccountGroup.Revenue ? credit - debit : debit - credit;
                return new IncomeStatementLine(first.LedgerAccountId, first.AccountCode, first.AccountName,
                    first.AccountGroup, amount);
            })
            .OrderBy(l => l.AccountCode, StringComparer.Ordinal)
            .ToList();

        var revenues = byAccount.Where(l => l.AccountGroup == AccountGroup.Revenue).ToList();
        var expenses = byAccount.Where(l => l.AccountGroup == AccountGroup.Expense).ToList();

        return new IncomeStatement(from, to, revenues, expenses,
            revenues.Sum(l => l.Amount), expenses.Sum(l => l.Amount));
    }
}
