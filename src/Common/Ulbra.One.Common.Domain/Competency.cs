using System.Globalization;
using System.Text.RegularExpressions;

namespace Ulbra.One.Common.Domain;

/// <summary>
/// Helpers for the monthly competency string (<c>"YYYY-MM"</c>) that is the time axis of a budget.
/// Zero-padded, so it is lexicographically == chronologically ordered — range filters and ordering
/// become single-column string comparisons. Shared across modules (Core owns the budget cells,
/// Budgeting owns requisitions/adjustments) plus the seed and (later) the xlsx/Firebase importers.
/// </summary>
public static partial class Competency
{
    [GeneratedRegex(@"^\d{4}-(0[1-9]|1[0-2])$")]
    private static partial Regex Pattern();

    /// <summary>True when <paramref name="value"/> is a well-formed <c>"YYYY-MM"</c> competency.</summary>
    public static bool IsValid(string? value) => value is not null && Pattern().IsMatch(value);

    /// <summary>Trims surrounding whitespace. Does NOT validate — call <see cref="IsValid"/> separately.</summary>
    public static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    /// <summary>First day of the competency month. Assumes a valid <c>"YYYY-MM"</c> (call <see cref="IsValid"/> first).</summary>
    public static DateOnly ToDateOnly(string competency)
    {
        var year = int.Parse(competency[..4], CultureInfo.InvariantCulture);
        var month = int.Parse(competency[5..], CultureInfo.InvariantCulture);
        return new DateOnly(year, month, 1);
    }

    /// <summary>Competency (<c>"YYYY-MM"</c>) of the given date.</summary>
    public static string FromDate(DateOnly date) => date.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>Competency (<c>"YYYY-MM"</c>) of the given instant (its UTC date component).</summary>
    public static string FromDate(DateTimeOffset instant) => FromDate(DateOnly.FromDateTime(instant.UtcDateTime));

    /// <summary>Chronological comparison of two valid competencies (ordinal string compare).</summary>
    public static int Compare(string a, string b) => string.CompareOrdinal(a, b);

    /// <summary>Adds <paramref name="months"/> months to a valid competency, returning the resulting
    /// <c>"YYYY-MM"</c>. Assumes <paramref name="competency"/> is valid (call <see cref="IsValid"/> first).</summary>
    public static string AddMonths(string competency, int months) => FromDate(ToDateOnly(competency).AddMonths(months));

    /// <summary>Number of months in the inclusive range <c>[start, endInclusive]</c> (1 when start == end).
    /// Assumes both are valid and <paramref name="endInclusive"/> is not before <paramref name="start"/>.</summary>
    public static int CountMonths(string start, string endInclusive)
    {
        var s = ToDateOnly(start);
        var e = ToDateOnly(endInclusive);
        return (e.Year - s.Year) * 12 + (e.Month - s.Month) + 1;
    }

    /// <summary>Enumerates every competency in the inclusive range <c>[start, endInclusive]</c>,
    /// chronologically. Assumes both are valid and end is not before start.</summary>
    public static IEnumerable<string> EnumerateRange(string start, string endInclusive)
    {
        var count = CountMonths(start, endInclusive);
        for (var i = 0; i < count; i++)
            yield return AddMonths(start, i);
    }
}
