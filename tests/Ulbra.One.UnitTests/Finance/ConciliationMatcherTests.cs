using Ulbra.One.Finance.Application.Conciliation;
using Xunit;

namespace Ulbra.One.UnitTests.Finance;

public class ConciliationMatcherTests
{
    private static InstallmentCandidate Cand(Guid id, decimal remaining, DateOnly due) => new(id, remaining, due);

    [Fact]
    public void Suggests_single_candidate_matching_amount_within_window()
    {
        var i = Guid.NewGuid();
        var cands = new[] { Cand(i, 1000m, new DateOnly(2026, 7, 12)), Cand(Guid.NewGuid(), 500m, new DateOnly(2026, 7, 10)) };
        var match = ConciliationMatcher.FindSuggestion(amount: -1000m, date: new DateOnly(2026, 7, 10), candidates: cands, windowDays: 5);
        Assert.Equal(i, match);
    }

    [Fact]
    public void No_suggestion_when_two_candidates_tie()
    {
        var cands = new[] { Cand(Guid.NewGuid(), 1000m, new DateOnly(2026, 7, 10)), Cand(Guid.NewGuid(), 1000m, new DateOnly(2026, 7, 11)) };
        Assert.Null(ConciliationMatcher.FindSuggestion(1000m, new DateOnly(2026, 7, 10), cands, 5));
    }

    [Fact]
    public void No_suggestion_outside_date_window()
    {
        var cands = new[] { Cand(Guid.NewGuid(), 1000m, new DateOnly(2026, 8, 1)) };
        Assert.Null(ConciliationMatcher.FindSuggestion(1000m, new DateOnly(2026, 7, 10), cands, 5));
    }

    [Fact]
    public void No_suggestion_when_amount_differs()
    {
        var cands = new[] { Cand(Guid.NewGuid(), 999m, new DateOnly(2026, 7, 10)) };
        Assert.Null(ConciliationMatcher.FindSuggestion(1000m, new DateOnly(2026, 7, 10), cands, 5));
    }

    [Fact]
    public void Matches_on_absolute_amount_so_sign_does_not_matter()
    {
        var i = Guid.NewGuid();
        var cands = new[] { Cand(i, 1000m, new DateOnly(2026, 7, 10)) };
        Assert.Equal(i, ConciliationMatcher.FindSuggestion(-1000m, new DateOnly(2026, 7, 10), cands, 5));
        Assert.Equal(i, ConciliationMatcher.FindSuggestion(1000m, new DateOnly(2026, 7, 10), cands, 5));
    }
}
