namespace Ulbra.One.FirestoreImport.Mapping;

/// <summary>Helpers for "YYYY-MM" competencies (month arithmetic for recurrence distribution).</summary>
public static class CompetencyRange
{
    public static bool IsValid(string? c)
        => c is { Length: 7 } && c[4] == '-'
           && int.TryParse(c.AsSpan(0, 4), out _)
           && int.TryParse(c.AsSpan(5, 2), out var m) && m is >= 1 and <= 12;

    private static int ToIndex(string c) => int.Parse(c[..4]) * 12 + int.Parse(c[5..]) - 1;
    private static string FromIndex(int i) => $"{i / 12:D4}-{i % 12 + 1:D2}";

    /// <summary>Inclusive count of months from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static int MonthsBetween(string from, string to) => ToIndex(to) - ToIndex(from) + 1;

    /// <summary>The competency <paramref name="months"/> after <paramref name="competency"/>.</summary>
    public static string Add(string competency, int months) => FromIndex(ToIndex(competency) + months);

    public static IEnumerable<string> Enumerate(string from, string to, int cap)
    {
        var f = ToIndex(from);
        var t = ToIndex(to);
        var n = 0;
        for (var i = f; i <= t && n < cap; i++, n++)
            yield return FromIndex(i);
    }
}
