using Ulbra.One.Finance.Infrastructure.Conciliation;
using Xunit;

namespace Ulbra.One.UnitTests.Finance;

public class OfxDateTests
{
    [Theory]
    [InlineData("20260612000000[-3:GMT]", 2026, 6, 12)] // bracketed timezone (real Santander format)
    [InlineData("20260615120151[-3:GMT]", 2026, 6, 15)]
    [InlineData("20260710", 2026, 7, 10)]               // plain YYYYMMDD (existing clean test)
    public void Parses_leading_day(string input, int year, int month, int day) =>
        Assert.Equal(new DateOnly(year, month, day), OfxDate.ParseDate(input));

    [Theory]
    [InlineData("00000000")] // all-zero sentinel
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026")]
    [InlineData("abcdefgh")]
    [InlineData("20261301")] // invalid month
    public void Invalid_is_null(string? input) => Assert.Null(OfxDate.ParseDate(input));
}
