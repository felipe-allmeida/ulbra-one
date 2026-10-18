namespace Ulbra.One.Finance.Application.Conciliation;

/// <summary>An open installment candidate for matching (remaining balance + due date).</summary>
public sealed record InstallmentCandidate(Guid InstallmentId, decimal Remaining, DateOnly DueDate);

/// <summary>A payment already registered against the paying account — the candidate pool for a statement debit
/// whose installment is already settled (typically baixado by the CNAB return).</summary>
public sealed record SettledPaymentCandidate(Guid PaymentId, decimal Amount, DateOnly Date);

public static class ConciliationMatcher
{
    public const int DefaultWindowDays = 5;

    /// <summary>Returns the single installment that matches <paramref name="amount"/> (exact remaining balance,
    /// compared on absolute value) within <paramref name="windowDays"/> of <paramref name="date"/>,
    /// or null if zero or more than one candidate matches.</summary>
    public static Guid? FindSuggestion(decimal amount, DateOnly date, IReadOnlyList<InstallmentCandidate> candidates, int windowDays = DefaultWindowDays)
    {
        var target = Math.Abs(amount);
        var matches = candidates
            .Where(c => c.Remaining == target && Math.Abs(c.DueDate.DayNumber - date.DayNumber) <= windowDays)
            .ToList();
        return matches.Count == 1 ? matches[0].InstallmentId : null;
    }

    /// <summary>
    /// Returns the single already-registered payment that settles <paramref name="amount"/> on exactly
    /// <paramref name="date"/>, or null when zero or more than one candidate matches.
    /// <para>
    /// Deliberately stricter than <see cref="FindSuggestion"/>: no date window. A settled payment carries the
    /// bank's own effective date (the CNAB return's), so it lands on the statement day exactly — and unlike the
    /// open-installment path, a wrong pairing here is not corrected by a human confirming it, since the link is
    /// applied straight away. Widening the window would trade a real guarantee for a cosmetic gain.
    /// </para>
    /// </summary>
    public static Guid? FindSettledPayment(decimal amount, DateOnly date, IReadOnlyList<SettledPaymentCandidate> candidates)
    {
        var target = Math.Abs(amount);
        var matches = candidates.Where(c => c.Amount == target && c.Date == date).ToList();
        return matches.Count == 1 ? matches[0].PaymentId : null;
    }

    /// <summary>Near-miss horizon for the explanation: a título further than this from the statement day is a
    /// different título, not a divergence worth showing.</summary>
    public const int NearWindowDays = 30;

    /// <summary>Amount tolerance for a near miss: the larger of this floor and <see cref="NearAmountRate"/> of the
    /// line. The floor keeps ordinary juros visible on small títulos, where a percentage band would be cents wide.</summary>
    public const decimal NearAmountFloor = 50m;

    /// <inheritdoc cref="NearAmountFloor"/>
    public const decimal NearAmountRate = 0.02m;

    /// <summary>How many near-misses one line may carry, so a page of rows cannot flood the payload.</summary>
    public const int MaxExplainedCandidates = 5;

    /// <summary>
    /// Explains why <paramref name="amount"/> on <paramref name="date"/> is still unmatched, and returns the
    /// near-miss installments behind that verdict, closest first.
    /// <para>
    /// Debits only — <see cref="ConciliationMismatch.CreditNotMatched"/> is the caller's answer for a credit,
    /// since the matcher never runs on those. This only DESCRIBES the pool as it stands; it never changes a
    /// status, so what an operator reads here can differ from the import-time verdict once títulos move.
    /// </para>
    /// </summary>
    public static ConciliationExplanation Explain(
        decimal amount,
        DateOnly date,
        IReadOnlyList<InstallmentCandidate> candidates,
        int windowDays = DefaultWindowDays,
        int nearWindowDays = NearWindowDays,
        int maxCandidates = MaxExplainedCandidates)
    {
        var target = Math.Abs(amount);
        var tolerance = Math.Max(NearAmountFloor, target * NearAmountRate);

        var near = candidates
            .Select(c => new CandidateNearMiss(
                c.InstallmentId, c.Remaining, c.DueDate,
                Math.Abs(c.Remaining - target),
                Math.Abs(c.DueDate.DayNumber - date.DayNumber)))
            .Where(c => c.AmountDelta <= tolerance && c.DayDelta <= nearWindowDays)
            .OrderBy(c => c.AmountDelta)
            .ThenBy(c => c.DayDelta)
            .ToList();

        // Ranked by how much the operator can do about it: an ambiguity is the actual blocker, an exact match is
        // one click away, a value divergence needs a decision, and a date gap is the weakest signal of the four.
        var exactInWindow = near.Count(c => c.AmountDelta == 0m && c.DayDelta <= windowDays);
        var mismatch =
            exactInWindow >= 2 ? ConciliationMismatch.Ambiguous
            : exactInWindow == 1 ? ConciliationMismatch.ExactAvailable
            : near.Exists(c => c.DayDelta <= windowDays) ? ConciliationMismatch.AmountDiffers
            : near.Count > 0 ? ConciliationMismatch.OutsideDateWindow
            : ConciliationMismatch.NoCandidate;

        return new ConciliationExplanation(mismatch, near.Take(maxCandidates).ToList());
    }
}

/// <summary>Why a statement line could not be paired automatically — the reconciliation screen shows this
/// instead of repeating "não conciliado" on every row, because the four cases need four different reactions.</summary>
public enum ConciliationMismatch
{
    /// <summary>Nothing in the pool comes close. Typically a bank fee, IOF or tariff, which has no título behind it.</summary>
    NoCandidate,

    /// <summary>A título sits on the right date but for a different value — juros, desconto or a partial payment.</summary>
    AmountDiffers,

    /// <summary>A título matches the value exactly, but its due date is too far from the statement day.</summary>
    OutsideDateWindow,

    /// <summary>Two or more títulos match exactly and nothing distinguishes them, so the matcher refused to guess.</summary>
    Ambiguous,

    /// <summary>A single título still matches exactly. The import-time matcher is greedy — an earlier identical
    /// debit consumed the parcela — so the pool, rebuilt now, holds a match that is one click away.</summary>
    ExactAvailable,

    /// <summary>The line is a credit. Automatic matching only covers debits against payables.</summary>
    CreditNotMatched,
}

/// <summary>A near-miss installment, with how far it sits from the statement line on each axis.</summary>
public sealed record CandidateNearMiss(
    Guid InstallmentId, decimal Remaining, DateOnly DueDate, decimal AmountDelta, int DayDelta);

/// <summary>The reason a line is still open, plus the near-misses that justify it, closest first.</summary>
public sealed record ConciliationExplanation(
    ConciliationMismatch Mismatch, IReadOnlyList<CandidateNearMiss> Candidates);
