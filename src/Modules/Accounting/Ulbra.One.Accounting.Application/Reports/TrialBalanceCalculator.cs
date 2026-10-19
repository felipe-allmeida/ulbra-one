using Ulbra.One.Accounting.Domain;

namespace Ulbra.One.Accounting.Application.Reports;

/// <summary>One account's row in the trial balance (balancete): the opening balance (saldo anterior — the
/// signed balance accumulated strictly before the range), the period's gross debit and credit movement, and
/// the closing balance (saldo final = opening + movement). Opening and <see cref="Balance"/> (closing) are
/// signed by the account's normal balance (positive = balance on its normal side). In the all-time query
/// (no range) opening is 0 and <see cref="Balance"/> equals the account's accumulated balance.</summary>
public sealed record TrialBalanceRow(
    Guid LedgerAccountId,
    string AccountCode,
    string? ReducedCode,
    string AccountName,
    AccountGroup AccountGroup,
    NormalBalance NormalBalance,
    decimal OpeningBalance,
    decimal TotalDebit,
    decimal TotalCredit,
    decimal Balance);

/// <summary>The full trial balance: one row per account plus the proof totals (the period's debit/credit sums).</summary>
public sealed record TrialBalance(
    IReadOnlyList<TrialBalanceRow> Rows,
    decimal TotalDebit,
    decimal TotalCredit)
{
    /// <summary>Double-entry proof: the sum of all debits equals the sum of all credits over the period.</summary>
    public bool IsBalanced => TotalDebit == TotalCredit;
}

/// <summary>Pure calculator that folds posted lines into a trial balance for an optional competence range.
/// No EF, no I/O. With no range it accumulates the whole history (opening 0, closing = accumulated balance);
/// with a range each account carries its opening balance (movements strictly before <c>from</c>), the in-range
/// gross debit/credit and the resulting closing balance. Uses the same signing rule as the Livro Razão
/// (<see cref="GeneralLedgerBuilder"/>) so a closing balance equals the Razão's on the same date.</summary>
public static class TrialBalanceCalculator
{
    public static TrialBalance Build(IEnumerable<PostedLine> lines, DateOnly? from = null, DateOnly? to = null)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var rangeFrom = from ?? DateOnly.MinValue;
        var rangeTo = to ?? DateOnly.MaxValue;
        if (rangeTo < rangeFrom)
            throw new ArgumentException("'to' must not precede 'from'.", nameof(to));

        // Accounts appear when they have any line up to `to`; movements after `to` are ignored.
        var rows = lines
            .Where(l => l.CompetenceDate <= rangeTo)
            .GroupBy(l => l.LedgerAccountId)
            .Select(g =>
            {
                var first = g.First();
                // Opening = signed balance strictly before `from` (0 when the range is open-ended).
                var opening = g.Where(l => l.CompetenceDate < rangeFrom)
                    .Sum(l => AccountBalance.Signed(l.NormalBalance, l.Side, l.Amount));
                var period = g.Where(l => l.CompetenceDate >= rangeFrom).ToList();
                var debit = period.Where(l => l.Side == EntrySide.Debit).Sum(l => l.Amount);
                var credit = period.Where(l => l.Side == EntrySide.Credit).Sum(l => l.Amount);
                var movement = period.Sum(l => AccountBalance.Signed(l.NormalBalance, l.Side, l.Amount));
                return new TrialBalanceRow(first.LedgerAccountId, first.AccountCode, first.ReducedCode,
                    first.AccountName, first.AccountGroup, first.NormalBalance, opening, debit, credit,
                    opening + movement);
            })
            .OrderBy(r => r.AccountCode, StringComparer.Ordinal)
            .ToList();

        return new TrialBalance(rows, rows.Sum(r => r.TotalDebit), rows.Sum(r => r.TotalCredit));
    }
}
