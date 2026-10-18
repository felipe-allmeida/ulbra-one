using Xunit;

namespace Ulbra.One.UnitTests.Common;

/// <summary>Business-hour SLA engine — the C# port of web/src/pages/requisitions/sla.ts. Counts whole hours
/// landing on a business day (Mon–Fri, excluding BR national holidays: 9 fixed incl. 11-20 + movable via
/// Computus), bucketized in America/São_Paulo so it matches the frontend chips.</summary>
public class BusinessTimeTests
{
    // BRT wall-clock instant (Brazil has no DST since 2019 → constant -03:00 for the dates under test).
    private static DateTimeOffset At(int year, int month, int day, int hour = 0, int minute = 0) =>
        new(year, month, day, hour, minute, 0, TimeSpan.FromHours(-3));

    // 2026-01-01 is a Thursday → Jan 5 Mon, Jan 8 Thu, Jan 9 Fri, Jan 10 Sat, Jan 11 Sun, Jan 12 Mon.

    [Fact]
    public void Counts_whole_hours_within_a_business_day()
    {
        Assert.Equal(5, BusinessTime.BusinessHoursBetween(At(2026, 1, 5, 0), At(2026, 1, 5, 5)));
    }

    [Fact]
    public void Floors_start_to_the_hour()
    {
        // 00:30 floors to 00:00, then counts buckets 00 and 01 up to 02:00.
        Assert.Equal(2, BusinessTime.BusinessHoursBetween(At(2026, 1, 5, 0, 30), At(2026, 1, 5, 2, 0)));
    }

    [Fact]
    public void Skips_weekend()
    {
        // Fri 22:00 → Mon 02:00: Fri 22,23 (2) + Sat/Sun (0) + Mon 00,01 (2) = 4.
        Assert.Equal(4, BusinessTime.BusinessHoursBetween(At(2026, 1, 9, 22), At(2026, 1, 12, 2)));
    }

    [Fact]
    public void Counts_full_business_days_across_a_weekend()
    {
        // Thu 00:00 → Tue 00:00: Thu + Fri + Mon = 3 business days × 24h (Sat/Sun excluded).
        Assert.Equal(72, BusinessTime.BusinessHoursBetween(At(2026, 1, 8, 0), At(2026, 1, 13, 0)));
    }

    [Fact]
    public void Excludes_fixed_holiday_christmas()
    {
        Assert.Equal(0, BusinessTime.BusinessHoursBetween(At(2026, 12, 25, 0), At(2026, 12, 26, 0)));
    }

    [Fact] // The bug fixed vs. the web port: 20/11 (Consciência Negra) is a national holiday since 2024.
    public void Excludes_fixed_holiday_consciencia_negra_20_11()
    {
        // 2026-11-20 is a Friday; a full-day span across it counts 0 business hours.
        Assert.Equal(0, BusinessTime.BusinessHoursBetween(At(2026, 11, 20, 0), At(2026, 11, 21, 0)));
        Assert.False(BusinessTime.IsBusinessDay(new DateOnly(2026, 11, 20)));
    }

    [Fact]
    public void Excludes_movable_good_friday()
    {
        // Easter 2026 = Apr 5 → Good Friday = Apr 3 (a Friday).
        Assert.Equal(0, BusinessTime.BusinessHoursBetween(At(2026, 4, 3, 0), At(2026, 4, 4, 0)));
    }

    [Fact]
    public void Excludes_movable_corpus_christi()
    {
        // Easter 2026 + 60 days = Jun 4 (a Thursday).
        Assert.Equal(0, BusinessTime.BusinessHoursBetween(At(2026, 6, 4, 0), At(2026, 6, 5, 0)));
        Assert.False(BusinessTime.IsBusinessDay(new DateOnly(2026, 6, 4)));
    }

    [Fact] // Parity with the frontend set: sla.ts excludes BOTH Carnival Monday and Tuesday.
    public void Excludes_movable_carnival_monday_and_tuesday()
    {
        // Easter 2026 = Apr 5 → Carnival Monday = Feb 16, Tuesday = Feb 17.
        Assert.False(BusinessTime.IsBusinessDay(new DateOnly(2026, 2, 16)));
        Assert.False(BusinessTime.IsBusinessDay(new DateOnly(2026, 2, 17)));
    }

    [Fact]
    public void Returns_zero_when_end_not_after_start()
    {
        Assert.Equal(0, BusinessTime.BusinessHoursBetween(At(2026, 1, 5, 5), At(2026, 1, 5, 5)));
        Assert.Equal(0, BusinessTime.BusinessHoursBetween(At(2026, 1, 5, 5), At(2026, 1, 5, 1)));
    }

    [Theory]
    [InlineData(2024, 3, 31)]
    [InlineData(2025, 4, 20)]
    [InlineData(2026, 4, 5)]
    [InlineData(2027, 3, 28)]
    public void Computes_easter_sunday(int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), BusinessTime.EasterSunday(year));
    }

    [Fact]
    public void SaoPauloDayStart_is_midnight_brt()
    {
        var start = BusinessTime.SaoPauloDayStart(new DateOnly(2026, 6, 15));
        Assert.Equal(At(2026, 6, 15, 0), start);
        Assert.Equal(TimeSpan.FromHours(-3), start.Offset); // Brazil has no DST since 2019
    }

    [Fact]
    public void IsBusinessDay_distinguishes_weekday_weekend_holiday()
    {
        Assert.True(BusinessTime.IsBusinessDay(new DateOnly(2026, 1, 5)));   // Monday
        Assert.False(BusinessTime.IsBusinessDay(new DateOnly(2026, 1, 10))); // Saturday
        Assert.False(BusinessTime.IsBusinessDay(new DateOnly(2026, 1, 11))); // Sunday
        Assert.False(BusinessTime.IsBusinessDay(new DateOnly(2026, 1, 1)));  // Ano Novo (fixed)
    }
}
