using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;

namespace Ulbra.One.UnitTests.Accounting;

public class PendingAccountingPostingTests
{
    [Fact]
    public void New_pending_posting_is_open_until_resolved()
    {
        var p = new PendingAccountingPosting(Guid.NewGuid(), "payment:abc", PostingKind.Settlement, 100m,
            "CashCodeNotFound:1.1.01", "Dr 2.1.01 / Cr 1.1.01");
        Assert.Null(p.ResolvedAt);
        p.MarkResolved(DateTimeOffset.UnixEpoch);
        Assert.Equal(DateTimeOffset.UnixEpoch, p.ResolvedAt);
    }

    [Fact]
    public void JournalEntry_carries_source_key()
    {
        var e = new JournalEntry(Guid.NewGuid(), new DateOnly(2026, 1, 1), "Incurral",
            JournalOrigin.Automatic, sourceReference: null, sourceKey: "payable-incurral:xyz");
        Assert.Equal("payable-incurral:xyz", e.SourceKey);
        Assert.Equal(JournalOrigin.Automatic, e.Origin);
    }
}
