using System.Collections.Concurrent;

namespace Ulbra.One.Common.Domain;

/// <summary>Business-hour clock for the requisition SLA (C# port of web/src/pages/requisitions/sla.ts): whole
/// hours on a business day (Mon–Fri minus BR national holidays), bucketized in America/São_Paulo so the
/// backend agrees with the frontend chips. Pure/DI-free; pass <c>clock.GetUtcNow()</c> as the upper bound.</summary>
public static class BusinessTime
{
    // Brazil has no DST since 2019, so BR local time is a constant UTC-3. Fixed offset (like the project's
    // ReceivableClock) — no tz-database lookup, so it never depends on the runtime image shipping tzdata.
    private static readonly TimeSpan BrazilOffset = TimeSpan.FromHours(-3);

    // 9 fixed national holidays (incl. 11-20 Black Awareness Day, national since 2024).
    private static readonly (int Month, int Day)[] FixedHolidays =
    [
        (1, 1),   // New Year
        (4, 21),  // Tiradentes
        (5, 1),   // Labour Day
        (9, 7),   // Independence Day
        (10, 12), // Our Lady of Aparecida
        (11, 2),  // All Souls' Day
        (11, 15), // Republic Proclamation
        (11, 20), // Black Awareness Day
        (12, 25), // Christmas
    ];

    private static readonly ConcurrentDictionary<int, IReadOnlySet<DateOnly>> HolidayCache = new();

    /// <summary>Whole business-hours between two instants: floors <paramref name="start"/> to the hour, then
    /// counts each 1-hour bucket whose start lands on a business day until it reaches <paramref name="end"/>.
    /// Returns 0 when <paramref name="end"/> is not after <paramref name="start"/>.</summary>
    public static int BusinessHoursBetween(DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start) return 0;

        var startLocal = start.ToOffset(BrazilOffset);
        var endLocal = end.ToOffset(BrazilOffset);
        var cur = new DateTimeOffset(startLocal.Year, startLocal.Month, startLocal.Day, startLocal.Hour, 0, 0, BrazilOffset);

        var hours = 0;
        var guard = 0;
        while (cur < endLocal && guard++ < 24 * 400)
        {
            if (IsBusinessDay(DateOnly.FromDateTime(cur.DateTime))) hours++;
            cur = cur.AddHours(1);
        }
        return hours;
    }

    /// <summary>Instant at 00:00 BRT of <paramref name="date"/> — the BRT boundary for period filters. Callers
    /// that pass it to EF/Npgsql must convert to UTC (<c>.ToUniversalTime()</c>): timestamptz only accepts UTC.</summary>
    public static DateTimeOffset SaoPauloDayStart(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue), BrazilOffset);

    /// <summary>True when <paramref name="date"/> is Mon–Fri and not a national holiday.</summary>
    public static bool IsBusinessDay(DateOnly date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !HolidaysFor(date.Year).Contains(date);

    /// <summary>The national holidays (fixed + movable) for a year, cached.</summary>
    public static IReadOnlySet<DateOnly> HolidaysFor(int year) => HolidayCache.GetOrAdd(year, BuildHolidays);

    private static IReadOnlySet<DateOnly> BuildHolidays(int year)
    {
        var set = new HashSet<DateOnly>();
        foreach (var (month, day) in FixedHolidays) set.Add(new DateOnly(year, month, day));
        var easter = EasterSunday(year);
        // Movable holidays — must match the frontend sla.ts set so backend and chips agree.
        set.Add(easter.AddDays(-48)); // Carnival Monday
        set.Add(easter.AddDays(-47)); // Carnival Tuesday
        set.Add(easter.AddDays(-2));  // Good Friday
        set.Add(easter.AddDays(60));  // Corpus Christi
        return set;
    }

    /// <summary>Easter Sunday via Computus (Meeus/Jones/Butcher, Gregorian) — matches <c>easter()</c> in sla.ts.</summary>
    public static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = ((h + l - 7 * m + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }
}
