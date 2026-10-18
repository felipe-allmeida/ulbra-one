using Ulbra.One.Finance.Application.Conciliation;
using Xunit;

namespace Ulbra.One.UnitTests.Finance;

/// <summary>
/// <see cref="ConciliationMatcher.Explain"/> is what the reconciliation screen shows instead of a bare
/// "não conciliado": it says WHY the line could not be paired, and hands back the near-misses that justify it.
/// </summary>
public class ConciliationExplainTests
{
    private static readonly DateOnly Day = new(2026, 8, 25);

    private static InstallmentCandidate Cand(decimal remaining, DateOnly due) =>
        new(Guid.NewGuid(), remaining, due);

    private static ConciliationExplanation Explain(decimal amount, params InstallmentCandidate[] candidates) =>
        ConciliationMatcher.Explain(amount, Day, candidates);

    [Fact]
    public void No_candidate_at_all_when_nothing_is_even_close()
    {
        // A bank fee ("TAR REG TIT COB RAPIDA") never has a título behind it.
        var e = Explain(9.66m, Cand(1430.25m, Day));
        Assert.Equal(ConciliationMismatch.NoCandidate, e.Mismatch);
        Assert.Empty(e.Candidates);
    }

    [Fact]
    public void No_candidate_when_the_pool_is_empty()
    {
        var e = Explain(9.66m);
        Assert.Equal(ConciliationMismatch.NoCandidate, e.Mismatch);
        Assert.Empty(e.Candidates);
    }

    [Fact]
    public void Ambiguous_when_two_installments_match_the_amount_exactly_inside_the_window()
    {
        var e = Explain(1000m, Cand(1000m, Day), Cand(1000m, Day.AddDays(2)));
        Assert.Equal(ConciliationMismatch.Ambiguous, e.Mismatch);
        Assert.Equal(2, e.Candidates.Count);
    }

    [Fact]
    public void Exact_available_when_a_single_installment_still_matches_exactly()
    {
        // The import-time matcher is greedy: an earlier identical debit can consume the parcela, leaving this
        // line unmatched even though the pool, rebuilt now, holds a perfect match. Say so — it is one click away.
        var e = Explain(1000m, Cand(1000m, Day.AddDays(3)), Cand(77m, Day));
        Assert.Equal(ConciliationMismatch.ExactAvailable, e.Mismatch);
        Assert.Equal(1000m, Assert.Single(e.Candidates).Remaining);
    }

    [Fact]
    public void Amount_differs_when_a_near_value_sits_inside_the_date_window()
    {
        // Real Santander pair: "PAGFOR PIX ... DIFEREN TIT" 431,51 against a 442,36 título — juros/desconto.
        var e = Explain(431.51m, Cand(442.36m, Day));
        Assert.Equal(ConciliationMismatch.AmountDiffers, e.Mismatch);
        var near = Assert.Single(e.Candidates);
        Assert.Equal(10.85m, near.AmountDelta);
        Assert.Equal(0, near.DayDelta);
    }

    [Fact]
    public void Amount_delta_is_absolute_so_a_cheaper_title_reads_the_same_as_a_dearer_one()
    {
        var over = Explain(442.36m, Cand(431.51m, Day));
        Assert.Equal(ConciliationMismatch.AmountDiffers, over.Mismatch);
        Assert.Equal(10.85m, Assert.Single(over.Candidates).AmountDelta);
    }

    [Fact]
    public void A_far_off_value_is_not_a_near_miss_even_on_the_exact_day()
    {
        // 2% of 1000 is 20, and the floor is 50 — a 300 gap is a different título, not a divergence.
        var e = Explain(1000m, Cand(700m, Day));
        Assert.Equal(ConciliationMismatch.NoCandidate, e.Mismatch);
        Assert.Empty(e.Candidates);
    }

    [Fact]
    public void Small_absolute_gaps_stay_near_misses_on_small_amounts()
    {
        // The R$50 floor: on a 120,00 título a 2% band would be 2,40 and would miss an ordinary juros.
        var e = Explain(120m, Cand(155m, Day));
        Assert.Equal(ConciliationMismatch.AmountDiffers, e.Mismatch);
        Assert.Equal(35m, Assert.Single(e.Candidates).AmountDelta);
    }

    [Fact]
    public void Percentage_band_widens_the_tolerance_on_large_amounts()
    {
        // 2% of 100.000 is 2.000 — wider than the floor, so a 1.500 divergence still reads as the same título.
        var e = Explain(100_000m, Cand(101_500m, Day));
        Assert.Equal(ConciliationMismatch.AmountDiffers, e.Mismatch);
        Assert.Equal(1500m, Assert.Single(e.Candidates).AmountDelta);
    }

    [Fact]
    public void Outside_date_window_when_the_amount_is_exact_but_the_due_date_is_far()
    {
        var e = Explain(1000m, Cand(1000m, Day.AddDays(20)));
        Assert.Equal(ConciliationMismatch.OutsideDateWindow, e.Mismatch);
        var late = Assert.Single(e.Candidates);
        Assert.Equal(0m, late.AmountDelta);
        Assert.Equal(20, late.DayDelta);
    }

    [Fact]
    public void Day_delta_is_absolute_so_an_early_due_date_reads_like_a_late_one()
    {
        var e = Explain(1000m, Cand(1000m, Day.AddDays(-20)));
        Assert.Equal(ConciliationMismatch.OutsideDateWindow, e.Mismatch);
        Assert.Equal(20, Assert.Single(e.Candidates).DayDelta);
    }

    [Fact]
    public void A_title_beyond_the_near_date_horizon_is_not_a_candidate()
    {
        var e = Explain(1000m, Cand(1000m, Day.AddDays(45)));
        Assert.Equal(ConciliationMismatch.NoCandidate, e.Mismatch);
        Assert.Empty(e.Candidates);
    }

    [Fact]
    public void Exact_in_window_outranks_a_merely_near_value()
    {
        // Both are candidates, but the reason names the strongest signal.
        var e = Explain(1000m, Cand(1000m, Day.AddDays(1)), Cand(1010m, Day));
        Assert.Equal(ConciliationMismatch.ExactAvailable, e.Mismatch);
        Assert.Equal(2, e.Candidates.Count);
    }

    [Fact]
    public void Ambiguity_outranks_everything_because_it_is_the_actual_blocker()
    {
        var e = Explain(1000m, Cand(1000m, Day), Cand(1000m, Day.AddDays(1)), Cand(1010m, Day));
        Assert.Equal(ConciliationMismatch.Ambiguous, e.Mismatch);
        Assert.Equal(3, e.Candidates.Count);
    }

    [Fact]
    public void A_near_value_inside_the_window_outranks_an_exact_one_outside_it()
    {
        var e = Explain(1000m, Cand(1010m, Day), Cand(1000m, Day.AddDays(20)));
        Assert.Equal(ConciliationMismatch.AmountDiffers, e.Mismatch);
    }

    [Fact]
    public void Candidates_come_back_closest_first_by_amount_then_by_date()
    {
        var e = Explain(1000m, Cand(1030m, Day), Cand(1010m, Day.AddDays(4)), Cand(1010m, Day.AddDays(1)));
        Assert.Equal(new[] { 1010m, 1010m, 1030m }, e.Candidates.Select(c => c.Remaining));
        Assert.Equal(new[] { 1, 4, 0 }, e.Candidates.Select(c => c.DayDelta));
    }

    [Fact]
    public void The_candidate_list_is_capped_so_one_row_cannot_flood_the_payload()
    {
        var pool = Enumerable.Range(1, 12).Select(i => Cand(1000m + i, Day)).ToArray();
        var e = Explain(1000m, pool);
        Assert.Equal(5, e.Candidates.Count);
        Assert.Equal(1001m, e.Candidates[0].Remaining);
    }

    [Fact]
    public void The_amount_sign_does_not_matter_just_like_in_the_matcher()
    {
        var e = ConciliationMatcher.Explain(-1000m, Day, [Cand(1000m, Day)]);
        Assert.Equal(ConciliationMismatch.ExactAvailable, e.Mismatch);
    }

    [Fact]
    public void Candidates_carry_their_installment_id_so_the_screen_can_reconcile_in_one_click()
    {
        var c = Cand(1000m, Day);
        var e = Explain(1000m, c);
        Assert.Equal(c.InstallmentId, Assert.Single(e.Candidates).InstallmentId);
        Assert.Equal(c.DueDate, e.Candidates[0].DueDate);
    }
}
