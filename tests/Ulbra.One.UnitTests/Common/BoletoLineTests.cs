using Ulbra.One.Common.Domain.Validation;
using Xunit;

namespace Ulbra.One.UnitTests.Common;

/// <summary>Tests for <see cref="BoletoLine"/> — the 47/48 length rule and advisory DV consistency.</summary>
public class BoletoLineTests
{
    // Bancário 47: banco+moeda "0339", three empty campos livres, fator+valor all zeros. DVs computed per
    // FEBRABAN: field DVs (mod-10) = 2/0/0; general DV (mod-11 over the 43-digit barcode "0339"+39 zeros) = 3.
    private const string Bancario47Valid =
        "0339" + "00000" + "2" + "0000000000" + "0" + "0000000000" + "0" + "3" + "00000000000000";

    // Real arrecadação/convênio line (48), value identifier "6" → block DVs mod-10. Barcode starts with 8.
    private const string Convenio48Valid = "896100000000599800010119053332010064260000157446";

    [Fact]
    public void Digits_strips_mask_separators()
        => Assert.Equal("0339123", BoletoLine.Digits("0339.123 "));

    [Theory]
    [InlineData("8123456789012345678901234567890123456789012345", true)] // starts with 8
    [InlineData("34191790010104351004791020150008291070026000123", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsConvenio_detects_the_lead_8(string? line, bool expected)
        => Assert.Equal(expected, BoletoLine.IsConvenio(line));

    [Theory]
    [InlineData("34191790010104351004791020150008291070026000123", true)]  // 47 bancário
    [InlineData("846700000017599200241114202410000000000000000000", true)] // 48 convênio (starts with 8)
    [InlineData("3111111111111111111111111111111111111111111111111", false)] // 49 digits
    [InlineData("312345678901234567890123456789012345678901234567", false)] // 48 without the 8
    // A 47-digit line starting with 8 is a truncated convênio (always 48): it would route to Seg O but
    // ConvenioTo44 needs 48 → invalid, not a bancário.
    [InlineData("82650000007889180798000002026177663112901001043", false)] // 47 + lead 8
    [InlineData("123", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid_is_length_plus_lead_8_only(string? line, bool expected)
        => Assert.Equal(expected, BoletoLine.IsValid(line));

    [Fact]
    public void IsValid_ignores_mask_separators()
        => Assert.True(BoletoLine.IsValid("34191.79001 01043.510047 91020.150008 2 91070026000123"));

    // ---- Advisory verifier-digit check (mod-10 / mod-11) --------------------------------

    [Fact]
    public void CheckDigitsConsistent_true_for_a_valid_bancario_line()
        => Assert.True(BoletoLine.CheckDigitsConsistent(Bancario47Valid));

    [Fact]
    public void CheckDigitsConsistent_true_for_a_real_convenio_line()
        => Assert.True(BoletoLine.CheckDigitsConsistent(Convenio48Valid));

    [Fact]
    public void CheckDigitsConsistent_false_when_a_bancario_field_dv_is_wrong()
    {
        // Flip the first field DV (index 9) from 2 to 3.
        var tampered = Bancario47Valid[..9] + "3" + Bancario47Valid[10..];
        Assert.False(BoletoLine.CheckDigitsConsistent(tampered));
    }

    [Fact]
    public void CheckDigitsConsistent_false_when_a_bancario_general_dv_is_wrong()
    {
        // Flip the general DV (index 32) from 3 to 4.
        var tampered = Bancario47Valid[..32] + "4" + Bancario47Valid[33..];
        Assert.False(BoletoLine.CheckDigitsConsistent(tampered));
    }

    [Fact]
    public void CheckDigitsConsistent_false_when_a_convenio_block_dv_is_wrong()
    {
        // Flip block 1's DV (index 11) from 0 to 1.
        var tampered = Convenio48Valid[..11] + "1" + Convenio48Valid[12..];
        Assert.False(BoletoLine.CheckDigitsConsistent(tampered));
    }

    // Real published linha digitável values (cross-checked against boleto libraries), so the check is
    // anchored to actual FEBRABAN documents and not only the synthetic vectors above.
    [Theory]
    [InlineData("23793381286000782713695000063305975520000370000")] // banco 237 (Bradesco)
    [InlineData("34195000080123320318964221470004584410000002000")] // banco 341 (Itaú)
    public void CheckDigitsConsistent_true_for_real_bancario_lines(string line)
        => Assert.True(BoletoLine.CheckDigitsConsistent(line));

    // Real arrecadação lines with value identifier 8 → block DVs are mod-11 (the branch the synthetic mod-10
    // vector above does not exercise). Both cross-checked against a barcode reconstruction.
    [Theory]
    [InlineData("858200000007572503282030560708202107539591904460")]
    [InlineData("838600000050096000190009000801782309000343062712")]
    public void CheckDigitsConsistent_true_for_real_mod11_convenio_lines(string line)
        => Assert.True(BoletoLine.CheckDigitsConsistent(line));

    [Fact]
    public void CheckDigitsConsistent_false_when_a_mod11_convenio_block_dv_is_wrong()
    {
        const string line = "858200000007572503282030560708202107539591904460";
        var tampered = line[..11] + (line[11] == '7' ? "6" : "7") + line[12..]; // flip block 1's DV
        Assert.False(BoletoLine.CheckDigitsConsistent(tampered));
    }

    [Theory]
    [InlineData("123")]        // too short
    [InlineData("")]           // blank
    [InlineData(null)]         // null
    public void CheckDigitsConsistent_false_for_a_malformed_line(string? line)
        => Assert.False(BoletoLine.CheckDigitsConsistent(line));

    [Fact]
    public void Canonical_stores_digits_only_so_a_masked_line_and_a_bare_one_are_one_value()
        => Assert.Equal("0339123", BoletoLine.Canonical("0339.123 "));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Canonical_of_a_blank_line_is_null(string? line)
        => Assert.Null(BoletoLine.Canonical(line));
}
