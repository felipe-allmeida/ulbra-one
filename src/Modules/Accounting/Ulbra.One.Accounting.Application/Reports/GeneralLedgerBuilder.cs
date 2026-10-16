using Ulbra.One.Accounting.Domain;

namespace Ulbra.One.Accounting.Application.Reports;

/// <summary>Pure calculator that folds flat Posted lines into the Livro Razão: grouped by account, each with an
/// opening balance (the signed sum of movements strictly before <c>from</c>), the in-range movements ordered
/// chronologically (CompetenceDate → Number → Side, debit before credit) carrying a running accumulated
/// balance, and the resulting closing balance. The balance is signed by the account's NormalBalance — the same
/// rule as TrialBalance/BalanceSheet — so a Razão closing balance equals the account's Balancete balance at the
/// same date. No EF, no I/O.</summary>
public static class GeneralLedgerBuilder
{
    public static IReadOnlyList<GeneralLedgerAccount> Build(
        IEnumerable<GeneralLedgerPostedLine> rows, DateOnly? from, DateOnly? to)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (from is { } f && to is { } t && t < f)
            throw new ArgumentException("'to' must not precede 'from'.", nameof(to));

        return rows
            .GroupBy(r => r.LedgerAccountId)
            .Select(g => BuildAccount(g, from))
            .OrderBy(a => a.AccountCode, StringComparer.Ordinal)
            .ToList();
    }

    private static GeneralLedgerAccount BuildAccount(IGrouping<Guid, GeneralLedgerPostedLine> group, DateOnly? from)
    {
        var first = group.First();

        // Opening balance = signed sum of movements strictly before `from` (0 when the range is open-ended).
        var opening = from is { } f ? group.Where(r => r.CompetenceDate < f).Sum(Signed) : 0m;
        var inRange = from is { } f2 ? group.Where(r => r.CompetenceDate >= f2) : group;

        var running = opening;
        var movements = new List<GeneralLedgerMovement>();
        foreach (var r in inRange
            .OrderBy(r => r.CompetenceDate)
            .ThenBy(r => r.Number)
            .ThenBy(r => r.Side) // Debit (0) before Credit (1)
            .ThenBy(r => r.EntryId))
        {
            running += Signed(r);
            movements.Add(new GeneralLedgerMovement(
                r.EntryId, r.Number, r.CompetenceDate, r.EntryDescription, r.SourceReference, r.CostCenterId,
                r.Side == EntrySide.Debit ? r.Amount : 0m,
                r.Side == EntrySide.Credit ? r.Amount : 0m,
                running,
                r.CounterpartyName, r.CounterpartyDocument));
        }

        return new GeneralLedgerAccount(
            first.LedgerAccountId, first.AccountCode, first.ReducedCode, first.AccountName, first.NormalBalance,
            opening, movements, movements.Sum(m => m.Debit), movements.Sum(m => m.Credit), running);
    }

    /// <summary>Signed contribution of a line to its account balance, via the shared
    /// <see cref="AccountBalance.Signed"/> rule (kept identical across Balancete/Razão/Balanço).</summary>
    private static decimal Signed(GeneralLedgerPostedLine r) =>
        AccountBalance.Signed(r.NormalBalance, r.Side, r.Amount);
}
