using Ulbra.One.Fiscal.Application.Assessment;
using Xunit;

namespace Ulbra.One.UnitTests.Fiscal;

public class AssessmentPeriodParserTests
{
    [Theory]
    [InlineData("2026-07", true, 2026, 7)]
    [InlineData("2010-01", true, 2010, 1)]
    [InlineData("2099-12", true, 2099, 12)]
    public void Parses_valid_competencia(string value, bool expected, int year, int month)
    {
        Assert.Equal(expected, AssessmentPeriodParser.TryParse(value, out var period));
        Assert.Equal(year, period.Year);
        Assert.Equal(month, period.Month);
    }

    [Theory]
    [InlineData("2005-03")] // before the EFD-Reinf perApur range (20[1-9][0-9])
    [InlineData("2100-03")] // after the range
    [InlineData("2026-13")] // invalid month
    [InlineData("2026-00")]
    [InlineData("2026")]    // malformed
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_out_of_range_or_malformed(string? value) =>
        Assert.False(AssessmentPeriodParser.TryParse(value, out _));
}
