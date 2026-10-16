using System;
using Ulbra.One.Finance.Domain.Cnab;
using Xunit;

namespace Ulbra.One.UnitTests.Finance.Cnab;

/// <summary>
/// Tests for <see cref="BoletoBarcode"/> — linha digitável → código de barras (44 pos).
/// Vectors use labeled segments so the reorder/strip is verifiable by construction.
/// </summary>
public class BoletoBarcodeTests
{
    // Boleto bancário 47-digit linha digitável, built segment by segment (0-based indices):
    //  banco+moeda(0-3)="0339", CL1(4-8)="11111", dv1(9)="2", CL2(10-19)="3333333333",
    //  dv2(20)="4", CL3(21-30)="5555555555", dv3(31)="6", dvGeral(32)="7",
    //  fator(33-36)="8888", valor(37-46)="9999999999".
    private const string BancarioLinha =
        "0339" + "11111" + "2" + "3333333333" + "4" + "5555555555" + "6" + "7" + "8888" + "9999999999";

    // Expected barcode: banco+moeda + dvGeral + fator+valor + CL1 + CL2 + CL3.
    private const string BancarioBarcode =
        "0339" + "7" + "8888" + "9999999999" + "11111" + "3333333333" + "5555555555";

    // Convênio/arrecadação 48-digit linha: 4 blocks of 11 significant + 1 DV.
    private const string ConvenioLinha =
        "81111111111" + "2" + "33333333333" + "4" + "55555555555" + "6" + "77777777777" + "8";

    private const string ConvenioBarcode =
        "81111111111" + "33333333333" + "55555555555" + "77777777777";

    [Fact]
    public void BancarioTo44_reorders_47_digit_linha_into_44_position_barcode()
    {
        var result = BoletoBarcode.BancarioTo44(BancarioLinha);

        Assert.Equal(44, result.Length);
        Assert.Equal(BancarioBarcode, result);
    }

    [Fact]
    public void BancarioTo44_strips_non_digits_before_converting()
    {
        var formatted = "0339.11111 2  33333.33333 4 55555.55555 6 7 8888 9999999999";

        Assert.Equal(BancarioBarcode, BoletoBarcode.BancarioTo44(formatted));
    }

    [Fact]
    public void BancarioTo44_passes_through_a_44_digit_value()
    {
        Assert.Equal(BancarioBarcode, BoletoBarcode.BancarioTo44(BancarioBarcode));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("012345678901234567890123456789012345678901234")] // 45
    public void BancarioTo44_throws_on_wrong_length(string linha)
    {
        Assert.Throws<ArgumentException>(() => BoletoBarcode.BancarioTo44(linha));
    }

    [Fact]
    public void ConvenioTo44_strips_block_dvs_from_48_digit_linha()
    {
        var result = BoletoBarcode.ConvenioTo44(ConvenioLinha);

        Assert.Equal(44, result.Length);
        Assert.Equal(ConvenioBarcode, result);
        Assert.StartsWith("8", result);
    }

    [Fact]
    public void ConvenioTo44_passes_through_a_44_digit_value()
    {
        Assert.Equal(ConvenioBarcode, BoletoBarcode.ConvenioTo44(ConvenioBarcode));
    }

    [Fact]
    public void ConvenioTo44_throws_on_wrong_length()
    {
        Assert.Throws<ArgumentException>(() => BoletoBarcode.ConvenioTo44("81111111111"));
    }

    [Theory]
    [InlineData("8123456789012345678901234567890123456789012345", true)]  // starts with 8
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsConvenio_detects_the_arrecadacao_lead_digit(string? linha, bool expected)
    {
        Assert.Equal(expected, BoletoBarcode.IsConvenio(linha));
    }

    [Fact]
    public void IsConvenio_is_false_for_a_bancario_linha()
    {
        Assert.False(BoletoBarcode.IsConvenio(BancarioLinha));
    }

    // A 48-digit line that does not begin with 8 is not arrecadação/convênio — and a boleto bancário is
    // always 47. It is therefore malformed; CnabFieldValidation.BoletoReason rejects it up front so it
    // never reaches BancarioTo44 (which requires 47 and would throw an unhandled ArgumentException → 500).
    private const string Malformed48NoLeadEight =
        "31111111111" + "2" + "33333333333" + "4" + "55555555555" + "6" + "77777777777" + "8"; // 48, starts with 3

    [Fact]
    public void IsConvenio_is_false_for_a_48_digit_linha_without_the_8_prefix()
    {
        Assert.False(BoletoBarcode.IsConvenio(Malformed48NoLeadEight));
    }

    [Fact]
    public void ToBarcode_dispatches_by_category()
    {
        Assert.Equal(BancarioBarcode, BoletoBarcode.ToBarcode(BancarioLinha));
        Assert.Equal(ConvenioBarcode, BoletoBarcode.ToBarcode(ConvenioLinha));
    }
}
