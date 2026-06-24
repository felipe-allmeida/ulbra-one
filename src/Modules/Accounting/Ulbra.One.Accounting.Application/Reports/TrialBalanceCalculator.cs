using Ulbra.One.Accounting.Domain;

namespace Ulbra.One.Accounting.Application.Reports;

/// <summary>One account's row in the trial balance (balancete): total debits, total credits and the
/// signed balance respecting the account's normal balance (positive = balance on its normal side).</summary>
public sealed record TrialBalanceRow(
    Guid LedgerAccountId,
    string AccountCode,
    string AccountName,
    AccountGroup AccountGroup,
    NormalBalance NormalBalance,
    decimal TotalDebit,
    decimal TotalCredit,
    decimal Balance);

/// <summary>The full trial balance: one row per account plus the proof totals.</summary>
public sealed record TrialBalance(
    IReadOnlyList<TrialBalanceRow> Rows,
    decimal TotalDebit,
    decimal TotalCredit)
{
    /// <summary>Double-entry proof: the sum of all debits equals the sum of all credits.</summary>
    public bool IsBalanced => TotalDebit == TotalCredit;
}

/// <summary>Pure calculator that folds posted lines into a trial balance. No EF, no I/O.</summary>
public static class TrialBalanceCalculator
{
    public static TrialBalance Build(IEnumerable<PostedLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var rows = lines
            .GroupBy(l => l.LedgerAccountId)
            .Select(g =>
            {
                var first = g.First();
                var debit = g.Where(l => l.Side == EntrySide.Debit).Sum(l => l.Amount);
                var credit = g.Where(l => l.Side == EntrySide.Credit).Sum(l => l.Amount);
                var balance = first.NormalBalance == NormalBalance.Debit ? debit - credit : credit - debit;
                return new TrialBalanceRow(first.LedgerAccountId, first.AccountCode, first.AccountName,
                    first.AccountGroup, first.NormalBalance, debit, credit, balance);
            })
            .OrderBy(r => r.AccountCode, StringComparer.Ordinal)
            .ToList();

        return new TrialBalance(rows, rows.Sum(r => r.TotalDebit), rows.Sum(r => r.TotalCredit));
    }
}
