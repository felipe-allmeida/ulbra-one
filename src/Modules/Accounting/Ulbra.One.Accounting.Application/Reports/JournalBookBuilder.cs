using Ulbra.One.Accounting.Domain;

namespace Ulbra.One.Accounting.Application.Reports;

/// <summary>Pure calculator that folds flat posted/reversed lines into the Livro Diário: entries grouped by
/// competence day, ordered chronologically (CompetenceDate → Number), with per-day and grand totals. Within
/// each entry, debits are listed before credits. No EF, no I/O.</summary>
public static class JournalBookBuilder
{
    public static JournalBook Build(IEnumerable<PostedEntryLine> rows, DateOnly? from, DateOnly? to)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var entries = rows
            .GroupBy(r => r.EntryId)
            .Select(g =>
            {
                var first = g.First();
                var lines = g
                    .OrderBy(r => r.Side) // Debit (0) before Credit (1)
                    .Select(r => new JournalBookLine(
                        r.LedgerAccountId, r.AccountCode, r.AccountName, r.Side, r.Amount, r.CostCenterId))
                    .ToList();
                var debit = lines.Where(l => l.Side == EntrySide.Debit).Sum(l => l.Amount);
                var credit = lines.Where(l => l.Side == EntrySide.Credit).Sum(l => l.Amount);
                return new JournalBookEntry(
                    first.EntryId, first.Number, first.CompetenceDate, first.Description, first.Status,
                    lines, debit, credit);
            })
            .OrderBy(e => e.CompetenceDate).ThenBy(e => e.Number)
            .ToList();

        var days = entries
            .GroupBy(e => e.CompetenceDate)
            .Select(g => new JournalBookDay(
                g.Key, g.ToList(), g.Sum(e => e.TotalDebit), g.Sum(e => e.TotalCredit)))
            .OrderBy(d => d.Date)
            .ToList();

        return new JournalBook(from, to, days, days.Sum(d => d.TotalDebit), days.Sum(d => d.TotalCredit));
    }
}
