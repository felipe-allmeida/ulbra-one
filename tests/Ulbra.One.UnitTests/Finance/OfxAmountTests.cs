using System.Globalization;
using Ulbra.One.Finance.Infrastructure.Conciliation;
using Xunit;

namespace Ulbra.One.UnitTests.Finance;

public class OfxAmountTests
{
    [Theory]
    [InlineData("-200,00", "-200.00")]        // pt-BR comma decimal (was silently parsed as -20000)
    [InlineData("2650.000,00", "2650000.00")] // dot thousands + comma decimal (was a FormatException)
    [InlineData("798,77", "798.77")]
    [InlineData("-1975,87", "-1975.87")]
    [InlineData("586577,87", "586577.87")]    // the LEDGERBAL from the real file
    [InlineData("-1000.00", "-1000.00")]      // invariant form (existing clean test) must still work
    [InlineData("2500.00", "2500.00")]
    [InlineData("1.234.567,89", "1234567.89")]
    [InlineData("1,234,567.89", "1234567.89")] // US grouped
    [InlineData("1.234.567", "1234567")]       // integer with thousands (>=3 digits after last sep)
    [InlineData("0", "0")]
    public void Parses_ptBR_and_invariant(string input, string expected) =>
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), OfxAmount.Parse(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_is_zero(string? input) => Assert.Equal(0m, OfxAmount.Parse(input));
}
