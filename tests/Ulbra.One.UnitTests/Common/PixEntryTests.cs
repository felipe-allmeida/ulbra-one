using Ulbra.One.Common.Domain.Validation;
using Xunit;

namespace Ulbra.One.UnitTests.Common;

/// <summary>Tests for <see cref="PixEntry"/> — routing a PIX installment's key/copia-e-cola inputs to storage.
/// A copia-e-cola (EMV/BR Code) pasted into the key field (no dedicated UI yet, and it is varchar(100)) is
/// auto-routed to the copia-e-cola slot (varchar 512) so the save never overflows into a 500 (AEL-963).</summary>
public class PixEntryTests
{
    // The real Itaú dynamic-QR payload that triggered the 500 (169 chars > varchar(100)).
    private const string ItauQr =
        "00020101021226770014BR.GOV.BCB.PIX2555api.itau/pix/qr/v2/2b4a01c0-cb2a-4d71-8338-d3a4d8f5c8af5204000053039865802BR5915TUNA PAGAMENTOS6013FLORIANOPOLIS62070503***63049F31";

    [Fact]
    public void A_copia_e_cola_pasted_into_the_key_field_is_routed_to_payloadEmv()
    {
        var (key, payloadEmv) = PixEntry.Resolve(ItauQr, null);
        Assert.Null(key);
        Assert.Equal(ItauQr, payloadEmv);
    }

    [Fact]
    public void A_plain_key_stays_in_the_key_field()
    {
        var (key, payloadEmv) = PixEntry.Resolve("pagamentos@fornecedor.com.br", null);
        Assert.Equal("pagamentos@fornecedor.com.br", key);
        Assert.Null(payloadEmv);
    }

    [Fact]
    public void An_explicit_copia_e_cola_is_kept()
    {
        var (key, payloadEmv) = PixEntry.Resolve(null, ItauQr);
        Assert.Null(key);
        Assert.Equal(ItauQr, payloadEmv);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Blank_becomes_null(string? key, string? payloadEmv)
    {
        var (k, c) = PixEntry.Resolve(key, payloadEmv);
        Assert.Null(k);
        Assert.Null(c);
    }

    [Fact]
    public void Trims_the_key()
        => Assert.Equal("a@b.com", PixEntry.Resolve("  a@b.com  ", null).Key);
}
