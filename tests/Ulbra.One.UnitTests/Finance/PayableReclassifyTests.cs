using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.UnitTests.Finance;

/// <summary>AEL-848 — domain guards for reclassifying a scheduled/paid payable's expense account (post-fato).</summary>
public class PayableReclassifyTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Reclassify_moves_account_and_returns_true_on_scheduled()
    {
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();
        var p = PayableTestFactory.Scheduled("actor", Now, Now, accountA);

        var changed = p.Reclassify(accountB);

        Assert.True(changed);
        Assert.Equal(accountB, p.AccountingAccountId);
    }

    [Fact]
    public void Reclassify_to_same_account_returns_false_but_still_clears_the_suggestion()
    {
        var account = Guid.NewGuid();
        var p = PayableTestFactory.Scheduled("actor", Now, Now, account);
        p.SetSuggestion(Guid.NewGuid(), 0.4, "low-confidence guess");

        var changed = p.Reclassify(account);

        // The account did not move, but confirming it must clear the review band so the row leaves the queue.
        Assert.False(changed);
        Assert.Equal(account, p.AccountingAccountId);
        Assert.Null(p.SuggestedAccountingAccountId);
        Assert.Null(p.ClassificationConfidence);
        Assert.Null(p.ClassificationRationale);
    }

    [Fact]
    public void Reclassify_throws_when_not_scheduled_or_paid()
    {
        var p = PayableTestFactory.Received("actor", Now);

        Assert.Throws<InvalidOperationException>(() => p.Reclassify(Guid.NewGuid()));
    }

    [Fact]
    public void Classify_throws_for_scheduled_or_paid()
    {
        var p = PayableTestFactory.Scheduled("actor", Now, Now, Guid.NewGuid());

        // A scheduled payable already has its incurral posted — the account must move through Reclassify.
        Assert.Throws<InvalidOperationException>(() => p.Classify(Guid.NewGuid()));
    }
}
