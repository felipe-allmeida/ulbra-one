using Ulbra.One.Common.Domain.Validation;
using Xunit;

namespace Ulbra.One.UnitTests.Common;

/// <summary>
/// Tests for <see cref="PixPayloadEmvParser"/> — the EMV/BR Code ("PIX copia e cola") parser. The two Santander
/// manual (G041) examples validate the payload-location-URL extraction against the authoritative source
/// (their CRC is invalid because the manual redacts the trailing fields). Self-built payloads carry a
/// correct CRC16-CCITT to exercise the CRC + static/dynamic routing end-to-end.
/// </summary>
public class PixPayloadEmvParserTests
{
    // Self-built, CRC-valid. Static (POI 11) → EVP key; Dynamic (POI 12) → payload-location URL.
    private const string StaticValid =
        "00020101021126580014br.gov.bcb.pix0136123e4567-e89b-12d3-a456-4266141740005204000053039865802BR5912FULANO TESTE6009SAO PAULO62070503***63042690";
    private const string DynamicValid =
        "00020101021226560014br.gov.bcb.pix2534pix.example.com/qr/v2/abc123def4565204000053039865802BR5912FULANO TESTE6009SAO PAULO62070503***6304D8B5";
    private const string DynamicWithAmount =
        "00020101021226470014br.gov.bcb.pix2525pix.example.com/qr/v2/xyz520400005303986540510.005802BR5912FULANO TESTE6009SAO PAULO62070503***630405C1";
    private const string DynamicWithScheme =
        "00020101021226620014br.gov.bcb.pix2540https://pix.example.com/qr/v2/withscheme5204000053039865802BR5912FULANO TESTE6009SAO PAULO62070503***63043AE6";
    private const string StaticUpperGui =
        "00020101021126580014BR.GOV.BCB.PIX0136123e4567-e89b-12d3-a456-4266141740005204000053039865802BR5912FULANO TESTE6009SAO PAULO62070503***6304CBB6";

    // Santander manual G041 examples (redacted → CRC invalid). Extraction must match the manual's "enviar no arquivo".
    private const string ManualImediato =
        "00020101021226850014br.gov.bcb.pix2563pix.santander.com.br/qr/v2/e2bcdb46-0f01-47b5-9999-2e7cccd3671b520400005303986540510.005802BR5925SODRO BENTO FILHO6009SAO PAULO62070503***63044B55";
    private const string ManualCobv =
        "00020101021226900014br.gov.bcb.pix2568pix.santander.com.br/qr/v2/cobv/98b66402-9999-47b2-b75a-9999f1e5bc9b52040000530398654041.005802BR5925SODRO BENTO FILHO6009SAO PAULO62070503***63043DA5";

    [Theory]
    [InlineData(StaticValid, true)]
    [InlineData(DynamicValid, true)]
    [InlineData("123e4567-e89b-12d3-a456-426614174000", false)] // plain EVP key
    [InlineData("fulano@example.com", false)]                    // email key
    [InlineData("12345678000199", false)]                        // CNPJ key
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LooksLikeBrCode_detects_the_emv_prefix(string? input, bool expected)
        => Assert.Equal(expected, PixPayloadEmvParser.LooksLikeBrCode(input));

    [Fact]
    public void Parse_dynamic_extracts_url_receiver_and_marks_crc_valid()
    {
        var r = PixPayloadEmvParser.Parse(DynamicValid);
        Assert.True(r.IsValid);
        Assert.True(r.CrcValid);
        Assert.Equal(PixPayloadEmvInitiation.Dynamic, r.Initiation);
        Assert.Equal("pix.example.com/qr/v2/abc123def456", r.PayloadLocationUrl);
        Assert.Null(r.PixKey);
        Assert.Equal("FULANO TESTE", r.ReceiverName);
        Assert.Equal("SAO PAULO", r.ReceiverCity);
        Assert.Null(r.Amount);
    }

    [Fact]
    public void Parse_static_extracts_key_and_marks_crc_valid()
    {
        var r = PixPayloadEmvParser.Parse(StaticValid);
        Assert.True(r.IsValid);
        Assert.True(r.CrcValid);
        Assert.Equal(PixPayloadEmvInitiation.Static, r.Initiation);
        Assert.Equal("123e4567-e89b-12d3-a456-426614174000", r.PixKey);
        Assert.Null(r.PayloadLocationUrl);
    }

    [Fact]
    public void Parse_dynamic_reads_amount_when_present()
        => Assert.Equal(10.00m, PixPayloadEmvParser.Parse(DynamicWithAmount).Amount);

    [Fact]
    public void Parse_strips_url_scheme()
        => Assert.Equal("pix.example.com/qr/v2/withscheme", PixPayloadEmvParser.Parse(DynamicWithScheme).PayloadLocationUrl);

    [Fact]
    public void Parse_matches_gui_case_insensitively_but_preserves_key_case()
    {
        var r = PixPayloadEmvParser.Parse(StaticUpperGui);
        Assert.True(r.IsValid);
        Assert.Equal("123e4567-e89b-12d3-a456-426614174000", r.PixKey);
    }

    [Fact]
    public void Parse_manual_imediato_example_extracts_the_63_char_url()
    {
        var r = PixPayloadEmvParser.Parse(ManualImediato);
        Assert.Equal(PixPayloadEmvInitiation.Dynamic, r.Initiation);
        Assert.Equal("pix.santander.com.br/qr/v2/e2bcdb46-0f01-47b5-9999-2e7cccd3671b", r.PayloadLocationUrl);
        Assert.Equal(63, r.PayloadLocationUrl!.Length);
    }

    [Fact]
    public void Parse_manual_cobv_example_extracts_the_68_char_url()
    {
        var r = PixPayloadEmvParser.Parse(ManualCobv);
        Assert.Equal(PixPayloadEmvInitiation.Dynamic, r.Initiation);
        Assert.Equal("pix.santander.com.br/qr/v2/cobv/98b66402-9999-47b2-b75a-9999f1e5bc9b", r.PayloadLocationUrl);
        Assert.Equal(68, r.PayloadLocationUrl!.Length);
    }

    [Fact]
    public void Parse_flags_bad_crc_but_still_extracts()
    {
        // Corrupt the last CRC hex digit of an otherwise-valid dynamic payload.
        var corrupted = DynamicValid[..^1] + (DynamicValid[^1] == '0' ? '1' : '0');
        var r = PixPayloadEmvParser.Parse(corrupted);
        Assert.False(r.CrcValid);
        Assert.Equal("pix.example.com/qr/v2/abc123def456", r.PayloadLocationUrl);
    }

    [Fact]
    public void Parse_computes_crc_over_utf8_bytes_so_an_accented_name_passes()
    {
        // A recebedor name with a non-ASCII char (JOSÉ) — the CRC is over the UTF-8 byte stream, so the code
        // must not fold to a per-char CRC (which would wrongly reject this legitimate payload).
        const string accented =
            "00020101021226470014br.gov.bcb.pix2525pix.example.com/qr/v2/abc5204000053039865802BR5910JOSÉ SILVA6009SAO PAULO630432B1";
        var r = PixPayloadEmvParser.Parse(accented);
        Assert.True(r.CrcValid);
        Assert.Equal("JOSÉ SILVA", r.ReceiverName);
    }

    [Fact]
    public void Parse_rejects_non_brcode_string()
        => Assert.False(PixPayloadEmvParser.Parse("fulano@example.com").IsValid);

    [Fact]
    public void Parse_rejects_payload_without_pix_merchant_template()
    {
        // Valid EMV TLV + CRC but tag 26 is a non-PIX GUID → no br.gov.bcb.pix template.
        const string noPix = "00020101021126330015br.com.outrogui011001234567895204000053039865802BR5902NA6002NA630419BB";
        Assert.False(PixPayloadEmvParser.Parse(noPix).IsValid);
    }

    [Fact]
    public void Parse_rejects_ambiguous_static_and_dynamic_together()
    {
        // A tag-26 template carrying BOTH a key (01) and a URL (25) is ambiguous → invalid, never guessed.
        const string both =
            "00020101021226630014br.gov.bcb.pix0114+55119999999992523pix.example.com/qr/v2/x5204000053039865802BR5902NA6002NA6304DAF9";
        Assert.False(PixPayloadEmvParser.Parse(both).IsValid);
    }
}
