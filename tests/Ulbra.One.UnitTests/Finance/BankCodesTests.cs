using Ulbra.One.Finance.Domain;
using Xunit;

namespace Ulbra.One.UnitTests.Finance;

public class BankCodesTests
{
    [Theory]
    [InlineData("033", "033")]
    [InlineData("33", "033")]
    [InlineData("0033", "033")]
    [InlineData(" 33 ", "033")]
    [InlineData("033 - Santander", "033")]
    [InlineData("341", "341")]
    [InlineData("0341", "341")]
    [InlineData("1", "001")]
    public void Normalize_canonicalizes_compe_codes(string input, string expected)
        => Assert.Equal(expected, BankCodes.Normalize(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Santander")]
    [InlineData("000")]
    public void Normalize_returns_empty_for_no_meaningful_digits(string? input)
        => Assert.Equal("", BankCodes.Normalize(input));

    [Theory]
    [InlineData("033")]
    [InlineData("33")]
    [InlineData("0033")]
    [InlineData(" 33 ")]
    public void IsSantander_true_for_any_form_of_033(string input)
        => Assert.True(BankCodes.IsSantander(input));

    [Theory]
    [InlineData("341")]
    [InlineData("")]
    [InlineData("Santander")]
    public void IsSantander_false_for_non_033(string input)
        => Assert.False(BankCodes.IsSantander(input));
}
