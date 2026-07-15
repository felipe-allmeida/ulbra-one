using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Accounting.Domain;
using Xunit;

namespace Ulbra.One.UnitTests.Accounting;

public class JournalBookBuilderTests
{
    private static PostedEntryLine Line(
        Guid entryId, int number, DateOnly competence, EntrySide side, decimal amount, string code = "1.1") =>
        new(entryId, number, competence, "desc", JournalStatus.Posted,
            Guid.NewGuid(), code, $"Account {code}", side, amount, null);

    [Fact]
    public void Groups_by_day_with_daily_and_grand_totals()
    {
        var e1 = Guid.NewGuid();
        var e2 = Guid.NewGuid();
        var jul1 = new DateOnly(2026, 7, 1);
        var jul2 = new DateOnly(2026, 7, 2);

        var rows = new[]
        {
            Line(e1, 1, jul1, EntrySide.Debit, 100m),
            Line(e1, 1, jul1, EntrySide.Credit, 100m),
            Line(e2, 2, jul2, EntrySide.Debit, 250m),
            Line(e2, 2, jul2, EntrySide.Credit, 250m),
        };

        var book = JournalBookBuilder.Build(rows, jul1, jul2);

        Assert.Equal(2, book.Days.Count);
        Assert.Equal(jul1, book.Days[0].Date);
        Assert.Equal(jul2, book.Days[1].Date);
        Assert.Equal(100m, book.Days[0].TotalDebit);
        Assert.Equal(100m, book.Days[0].TotalCredit);
        Assert.Equal(350m, book.TotalDebit);
        Assert.Equal(350m, book.TotalCredit);
    }

    [Fact]
    public void Orders_entries_by_competence_then_number()
    {
        var early = Guid.NewGuid();
        var late = Guid.NewGuid();
        // Deliberately feed the higher-competence entry first to prove ordering.
        var rows = new[]
        {
            Line(late, 5, new DateOnly(2026, 8, 1), EntrySide.Debit, 10m),
            Line(late, 5, new DateOnly(2026, 8, 1), EntrySide.Credit, 10m),
            Line(early, 9, new DateOnly(2026, 7, 1), EntrySide.Debit, 20m),
            Line(early, 9, new DateOnly(2026, 7, 1), EntrySide.Credit, 20m),
        };

        var book = JournalBookBuilder.Build(rows, null, null);

        Assert.Equal(new DateOnly(2026, 7, 1), book.Days[0].Date);
        Assert.Equal(9, book.Days[0].Entries[0].Number);
    }

    [Fact]
    public void Lists_debits_before_credits_within_an_entry()
    {
        var e = Guid.NewGuid();
        var day = new DateOnly(2026, 7, 1);
        // Credit fed first; builder must present debit first.
        var rows = new[]
        {
            Line(e, 1, day, EntrySide.Credit, 100m),
            Line(e, 1, day, EntrySide.Debit, 100m),
        };

        var book = JournalBookBuilder.Build(rows, null, null);
        var entry = book.Days[0].Entries[0];

        Assert.Equal(EntrySide.Debit, entry.Lines[0].Side);
        Assert.Equal(EntrySide.Credit, entry.Lines[1].Side);
        Assert.Equal(100m, entry.TotalDebit);
        Assert.Equal(100m, entry.TotalCredit);
    }

    [Fact]
    public void Empty_input_yields_empty_book()
    {
        var book = JournalBookBuilder.Build([], null, null);
        Assert.Empty(book.Days);
        Assert.Equal(0m, book.TotalDebit);
        Assert.Equal(0m, book.TotalCredit);
    }
}
