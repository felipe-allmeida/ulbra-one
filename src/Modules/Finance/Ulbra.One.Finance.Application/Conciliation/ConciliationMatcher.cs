namespace Ulbra.One.Finance.Application.Conciliation;

/// <summary>An open installment candidate for matching (remaining balance + due date).</summary>
public sealed record InstallmentCandidate(Guid InstallmentId, decimal Remaining, DateOnly DueDate);

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
}
