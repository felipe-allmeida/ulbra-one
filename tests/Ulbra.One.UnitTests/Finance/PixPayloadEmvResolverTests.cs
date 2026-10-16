using Ulbra.One.Finance.Application.Remittances;
using Xunit;

namespace Ulbra.One.UnitTests.Finance;

/// <summary>Tests for <see cref="PixPayloadEmvResolver"/> — the CNAB routing rule over the EMV parser:
/// static → a plain key (forma 45), dynamic → a payload URL (forma 47), malformed/CRC-bad → a skip.</summary>
public class PixPayloadEmvResolverTests
{
    private const string StaticValid =
        "00020101021126580014br.gov.bcb.pix0136123e4567-e89b-12d3-a456-4266141740005204000053039865802BR5912FULANO TESTE6009SAO PAULO62070503***63042690";
    private const string DynamicValid =
        "00020101021226560014br.gov.bcb.pix2534pix.example.com/qr/v2/abc123def4565204000053039865802BR5912FULANO TESTE6009SAO PAULO62070503***6304D8B5";
    // Santander manual imediato example — redacted, so its CRC does not validate.
    private const string ManualImediato =
        "00020101021226850014br.gov.bcb.pix2563pix.santander.com.br/qr/v2/e2bcdb46-0f01-47b5-9999-2e7cccd3671b520400005303986540510.005802BR5925SODRO BENTO FILHO6009SAO PAULO62070503***63044B55";

    [Fact]
    public void Static_resolves_to_the_embedded_key()
    {
        var r = PixPayloadEmvResolver.Resolve(StaticValid);
        Assert.Equal(PixPayloadEmvKind.StaticKey, r.Kind);
        Assert.Equal("123e4567-e89b-12d3-a456-426614174000", r.PixKey);
    }

    [Fact]
    public void Dynamic_resolves_to_the_payload_url_and_receiver()
    {
        var r = PixPayloadEmvResolver.Resolve(DynamicValid);
        Assert.Equal(PixPayloadEmvKind.DynamicQr, r.Kind);
        Assert.Equal("pix.example.com/qr/v2/abc123def456", r.PayloadLocationUrl);
        Assert.Equal("FULANO TESTE", r.ReceiverName);
    }

    [Fact]
    public void A_plain_key_is_not_a_copia_e_cola()
    {
        var r = PixPayloadEmvResolver.Resolve("fulano@example.com");
        Assert.Equal(PixPayloadEmvKind.Invalid, r.Kind);
        Assert.Equal(PixPayloadEmvResolver.ReasonInvalid, r.Reason);
    }

    [Fact]
    public void A_bad_crc_payload_is_rejected()
    {
        var corrupted = DynamicValid[..^1] + (DynamicValid[^1] == '0' ? '1' : '0');
        var r = PixPayloadEmvResolver.Resolve(corrupted);
        Assert.Equal(PixPayloadEmvKind.Invalid, r.Kind);
        Assert.Equal(PixPayloadEmvResolver.ReasonCrc, r.Reason);
    }

    [Fact]
    public void A_redacted_manual_example_fails_the_crc_gate()
    {
        // The manual's illustrative payloads are redacted → CRC invalid → never emitted. (Real payloads pass.)
        Assert.Equal(PixPayloadEmvKind.Invalid, PixPayloadEmvResolver.Resolve(ManualImediato).Kind);
    }

    [Fact]
    public void A_static_poi_carrying_only_a_url_and_no_key_is_rejected_not_a_blank_key()
    {
        // Defense-in-depth (AEL-963): a forged payload (POI 11 "static" but only a URL, no key sub-tag) must
        // NOT resolve to a StaticKey with an empty key — that would emit a blank-key forma 45.
        const string forged =
            "00020101021126450014br.gov.bcb.pix2523pix.example.com/qr/v2/x5204000053039865802BR5902NA6002NA630431FA";
        Assert.Equal(PixPayloadEmvKind.Invalid, PixPayloadEmvResolver.Resolve(forged).Kind);
    }
}
