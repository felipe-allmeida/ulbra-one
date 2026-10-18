using Ulbra.One.Common.Domain;
using Xunit;

namespace Ulbra.One.UnitTests.Budgeting;

public class CompetencyTests
{
    [Theory]
    [InlineData("2026-07")]
    [InlineData("2026-01")]
    [InlineData("2026-12")]
    [InlineData("2000-01")]
    [InlineData("2100-12")] // no artificial year cap — long-horizon budgets must validate
    public void IsValid_accepts_wellformed(string value) => Assert.True(Competency.IsValid(value));

    [Theory]
    [InlineData("2026-13")]
    [InlineData("2026-00")]
    [InlineData("2026-7")]
    [InlineData("26-01")]
    [InlineData("202607")]
    [InlineData("2026/07")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc-07")]
    [InlineData(null)]
    public void IsValid_rejects_malformed(string? value) => Assert.False(Competency.IsValid(value));

    [Fact]
    public void Normalize_trims() => Assert.Equal("2026-07", Competency.Normalize("  2026-07 "));

    [Fact]
    public void ToDateOnly_returns_first_of_month() => Assert.Equal(new DateOnly(2026, 7, 1), Competency.ToDateOnly("2026-07"));

    [Fact]
    public void FromDate_formats_zero_padded()
    {
        Assert.Equal("2026-01", Competency.FromDate(new DateOnly(2026, 1, 15)));
        Assert.Equal("2026-07", Competency.FromDate(new DateTimeOffset(2026, 7, 9, 3, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Ordering_is_chronological()
    {
        Assert.True(Competency.Compare("2026-01", "2026-02") < 0);
        Assert.True(Competency.Compare("2026-02", "2027-01") < 0);
        Assert.Equal(0, Competency.Compare("2026-05", "2026-05"));
    }

    [Theory]
    [InlineData("2026-07", 0, "2026-07")]
    [InlineData("2026-07", 11, "2027-06")]
    [InlineData("2026-12", 1, "2027-01")] // year rollover
    [InlineData("2026-01", -1, "2025-12")]
    public void AddMonths_shifts_and_rolls_over(string start, int months, string expected) =>
        Assert.Equal(expected, Competency.AddMonths(start, months));

    [Theory]
    [InlineData("2026-07", "2026-07", 1)] // inclusive: same month = 1
    [InlineData("2026-07", "2027-06", 12)] // start + 11 = 12 months
    [InlineData("2026-11", "2027-02", 4)] // crossing the year
    public void CountMonths_is_inclusive(string start, string end, int expected) =>
        Assert.Equal(expected, Competency.CountMonths(start, end));

    [Fact]
    public void EnumerateRange_yields_each_month_chronologically()
    {
        Assert.Equal(new[] { "2026-07" }, Competency.EnumerateRange("2026-07", "2026-07"));
        Assert.Equal(new[] { "2026-11", "2026-12", "2027-01", "2027-02" }, Competency.EnumerateRange("2026-11", "2027-02"));
        Assert.Equal(12, Competency.EnumerateRange("2026-07", Competency.AddMonths("2026-07", 11)).Count());
    }
}
