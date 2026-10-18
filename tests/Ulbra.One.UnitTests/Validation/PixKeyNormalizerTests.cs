using Ulbra.One.Common.Domain.Validation;

namespace Ulbra.One.UnitTests.Validation;

public class PixKeyNormalizerTests
{
    [Theory]
    [InlineData("  Financeiro@Fornecedor.com.br ", "Financeiro@Fornecedor.com.br")] // trim only, case kept
    [InlineData("a@b.com", "a@b.com")]
    public void Normalize_Email_TrimsOnly(string input, string expected) =>
        Assert.Equal(expected, PixKeyNormalizer.Normalize(input));

    [Theory]
    [InlineData("+55 51 99999-9999", "5551999999999")] // '+' and formatting dropped
    [InlineData("+5551999999999", "5551999999999")]    // E.164
    [InlineData("(51) 99999-9999", "51999999999")]     // no '+', formatting dropped
    public void Normalize_Phone_DigitsOnly(string input, string expected) =>
        Assert.Equal(expected, PixKeyNormalizer.Normalize(input));

    [Theory]
    [InlineData("123.456.789-09", "12345678909")]       // CPF
    [InlineData("12.345.678/0001-99", "12345678000199")] // CNPJ
    public void Normalize_Document_DigitsOnly(string input, string expected) =>
        Assert.Equal(expected, PixKeyNormalizer.Normalize(input));

    [Theory]
    [InlineData("123e4567-e89b-12d3-a456-426614174000")]     // random EVP (UUID) — kept verbatim
    [InlineData("71D6E7A7-1AF3-4B2C-8F0E-4B9E2A1C3D4E")]
    public void Normalize_RandomKey_KeptIntact(string input) =>
        Assert.Equal(input, PixKeyNormalizer.Normalize(input));

    [Fact]
    public void Normalize_RandomKey_TrimsSurroundingWhitespace()
    {
        const string key = "123e4567-e89b-12d3-a456-426614174000";
        Assert.Equal(key, PixKeyNormalizer.Normalize($"  {key}  "));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void Normalize_Empty_ReturnsEmpty(string? input, string expected) =>
        Assert.Equal(expected, PixKeyNormalizer.Normalize(input));

    [Fact]
    public void Normalize_PayloadEmvPayload_KeptIntact()
    {
        // A pasted PIX "copia e cola" (EMV/BR Code, AEL-963) must NEVER be digit-stripped — that would mangle
        // it into a shorter, wrong key that could misroute or bank-reject. It survives verbatim (trim only).
        const string payload =
            "00020101021226560014br.gov.bcb.pix2534pix.example.com/qr/v2/abc123def4565204000053039865802BR5912FULANO TESTE6009SAO PAULO62070503***6304D8B5";
        Assert.Equal(payload, PixKeyNormalizer.Normalize($"  {payload}  "));
    }
}
