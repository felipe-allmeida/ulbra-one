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

    // ── FindSettledPayment: o débito cujo título já foi baixado (retorno CNAB) ──

    private static SettledPaymentCandidate Pago(Guid id, decimal amount, DateOnly date) => new(id, amount, date);

    [Fact]
    public void Links_the_single_payment_settled_on_that_exact_day()
    {
        var pay = Guid.NewGuid();
        var pool = new[] { Pago(pay, 150m, new DateOnly(2026, 8, 25)) };
        Assert.Equal(pay, ConciliationMatcher.FindSettledPayment(-150m, new DateOnly(2026, 8, 25), pool));
    }

    [Fact]
    public void No_settled_link_when_the_day_differs()
    {
        // Stricter than FindSuggestion on purpose: no date window, because the link is applied without a human
        // confirming it. A payment carries the bank's own effective date, so it lands on the day exactly.
        var pool = new[] { Pago(Guid.NewGuid(), 150m, new DateOnly(2026, 8, 25)) };
        Assert.Null(ConciliationMatcher.FindSettledPayment(-150m, new DateOnly(2026, 8, 26), pool));
    }

    [Fact]
    public void No_settled_link_when_the_amount_differs()
    {
        var pool = new[] { Pago(Guid.NewGuid(), 149.99m, new DateOnly(2026, 8, 25)) };
        Assert.Null(ConciliationMatcher.FindSettledPayment(-150m, new DateOnly(2026, 8, 25), pool));
    }

    [Fact]
    public void No_settled_link_when_two_payments_are_indistinguishable()
    {
        // Two identical baixas on the same day: linking either would be a guess, so link neither.
        var pool = new[]
        {
            Pago(Guid.NewGuid(), 150m, new DateOnly(2026, 8, 25)),
            Pago(Guid.NewGuid(), 150m, new DateOnly(2026, 8, 25)),
        };
        Assert.Null(ConciliationMatcher.FindSettledPayment(-150m, new DateOnly(2026, 8, 25), pool));
    }
}
