using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Xunit;

namespace Ulbra.One.UnitTests.Accounting;

public class JournalEntryTests
{
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid Cash = Guid.NewGuid();
    private static readonly Guid Revenue = Guid.NewGuid();
    private static readonly DateOnly Competence = new(2026, 6, 1);
    private static readonly DateTimeOffset Now = new(2026, 6, 19, 12, 0, 0, TimeSpan.Zero);

    private static JournalEntry NewEntry() => new(Company, Competence, "Sale");

    private static JournalEntry Balanced(decimal amount = 100m)
    {
        var e = NewEntry();
        e.AddLine(new JournalLine(Cash, EntrySide.Debit, amount));
        e.AddLine(new JournalLine(Revenue, EntrySide.Credit, amount));
        return e;
    }

    [Fact]
    public void New_entry_is_draft_without_number()
    {
        var e = NewEntry();
        Assert.Equal(JournalStatus.Draft, e.Status);
        Assert.Null(e.Number);
        Assert.Null(e.PostedAt);
    }

    [Fact]
    public void Line_requires_positive_amount()
    {
        Assert.Throws<ArgumentException>(() => new JournalLine(Cash, EntrySide.Debit, 0m));
        Assert.Throws<ArgumentException>(() => new JournalLine(Cash, EntrySide.Debit, -5m));
    }

    [Fact]
    public void Post_rejects_unbalanced_entry()
    {
        var e = NewEntry();
        e.AddLine(new JournalLine(Cash, EntrySide.Debit, 100m));
        e.AddLine(new JournalLine(Revenue, EntrySide.Credit, 90m));
        var ex = Assert.Throws<JournalEntryException>(() => e.Post(1, Now));
        Assert.Equal("JournalEntry.Unbalanced", ex.Error.Code);
        Assert.Equal(JournalStatus.Draft, e.Status);
    }

    [Fact]
    public void Post_rejects_fewer_than_two_lines()
    {
        var e = NewEntry();
        e.AddLine(new JournalLine(Cash, EntrySide.Debit, 100m));
        var ex = Assert.Throws<JournalEntryException>(() => e.Post(1, Now));
        Assert.Equal("JournalEntry.TooFewLines", ex.Error.Code);
    }

    [Fact]
    public void ValidateStructure_reports_distinct_codes_per_rule()
    {
        var tooFew = NewEntry();
        tooFew.AddLine(new JournalLine(Cash, EntrySide.Debit, 100m));
        Assert.Equal("JournalEntry.TooFewLines",
            Assert.Throws<JournalEntryException>(tooFew.ValidateStructure).Error.Code);

        var unbalanced = NewEntry();
        unbalanced.AddLine(new JournalLine(Cash, EntrySide.Debit, 100m));
        unbalanced.AddLine(new JournalLine(Revenue, EntrySide.Credit, 90m));
        Assert.Equal("JournalEntry.Unbalanced",
            Assert.Throws<JournalEntryException>(unbalanced.ValidateStructure).Error.Code);

        var balanced = Balanced();
        balanced.ValidateStructure(); // no throw
    }

    [Fact]
    public void ValidateDraftComposition_enforces_count_but_not_balance()
    {
        // Too few lines → TooFewLines (same code as Post).
        var tooFew = NewEntry();
        tooFew.AddLine(new JournalLine(Cash, EntrySide.Debit, 100m));
        Assert.Equal("JournalEntry.TooFewLines",
            Assert.Throws<JournalEntryException>(tooFew.ValidateDraftComposition).Error.Code);

        // A draft may be saved unbalanced — composition does not enforce balancing.
        var unbalanced = NewEntry();
        unbalanced.AddLine(new JournalLine(Cash, EntrySide.Debit, 100m));
        unbalanced.AddLine(new JournalLine(Revenue, EntrySide.Credit, 90m));
        unbalanced.ValidateDraftComposition(); // no throw
    }

    [Fact]
    public void Post_rejects_non_positive_number()
    {
        var e = Balanced();
        Assert.Throws<ArgumentException>(() => e.Post(0, Now));
    }

    [Fact]
    public void Post_freezes_and_assigns_number_and_posted_at()
    {
        var e = Balanced();
        e.Post(7, Now);
        Assert.Equal(JournalStatus.Posted, e.Status);
        Assert.Equal(7, e.Number);
        Assert.Equal(Now, e.PostedAt);
        Assert.True(e.IsBalanced);
    }

    [Fact]
    public void AddLine_rejected_after_post()
    {
        var e = Balanced();
        e.Post(1, Now);
        Assert.Throws<InvalidOperationException>(() => e.AddLine(new JournalLine(Cash, EntrySide.Debit, 5m)));
    }

    [Fact]
    public void RemoveLine_and_UpdateHeader_rejected_after_post()
    {
        var e = Balanced();
        var lineId = e.Lines[0].Id;
        e.Post(1, Now);
        Assert.Throws<InvalidOperationException>(() => e.RemoveLine(lineId));
        Assert.Throws<InvalidOperationException>(() => e.UpdateHeader(Competence, "x", null));
    }

    [Fact]
    public void Post_rejected_when_already_posted()
    {
        var e = Balanced();
        e.Post(1, Now);
        Assert.Throws<InvalidOperationException>(() => e.Post(2, Now));
    }

    [Fact]
    public void RemoveLine_works_in_draft()
    {
        var e = Balanced();
        var id = e.Lines[0].Id;
        e.RemoveLine(id);
        Assert.Single(e.Lines);
    }

    [Fact]
    public void Reverse_only_allowed_on_posted()
    {
        var draft = Balanced();
        Assert.Throws<InvalidOperationException>(() => draft.Reverse("oops", Competence, 2, Now));
    }

    [Fact]
    public void Reverse_requires_reason()
    {
        var e = Balanced();
        e.Post(1, Now);
        Assert.Throws<ArgumentException>(() => e.Reverse("  ", Competence, 2, Now));
    }

    [Fact]
    public void Reverse_produces_inverted_balanced_posted_entry_and_links_both()
    {
        var e = Balanced(250m);
        e.Post(1, Now);

        var reversal = e.Reverse("mistake", Competence, 2, Now);

        Assert.Equal(JournalStatus.Reversed, e.Status);
        Assert.Equal(reversal.Id, e.ReversedByEntryId);

        Assert.Equal(JournalStatus.Posted, reversal.Status);
        Assert.Equal(2, reversal.Number);
        Assert.Equal(e.Id, reversal.ReversalOfEntryId);
        Assert.True(reversal.IsBalanced);

        // Sides are mirrored: the original debit on Cash becomes a credit, etc.
        var cashLine = Assert.Single(reversal.Lines, l => l.LedgerAccountId == Cash);
        Assert.Equal(EntrySide.Credit, cashLine.Side);
        Assert.Equal(250m, cashLine.Amount);
        var revLine = Assert.Single(reversal.Lines, l => l.LedgerAccountId == Revenue);
        Assert.Equal(EntrySide.Debit, revLine.Side);
    }

    [Fact]
    public void Reversed_entry_cannot_be_reversed_again()
    {
        var e = Balanced();
        e.Post(1, Now);
        e.Reverse("first", Competence, 2, Now);
        Assert.Throws<InvalidOperationException>(() => e.Reverse("second", Competence, 3, Now));
    }
}
