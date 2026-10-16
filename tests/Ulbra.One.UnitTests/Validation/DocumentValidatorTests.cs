using Ulbra.One.Common.Domain.Validation;

namespace Ulbra.One.UnitTests.Validation;

public class DocumentValidatorTests
{
    [Theory]
    [InlineData("529.982.247-25")] // valid CPF (formatted)
    [InlineData("52998224725")]    // valid CPF (digits only)
    public void IsValidCpf_True_For_Valid(string cpf) => Assert.True(DocumentValidator.IsValidCpf(cpf));

    [Theory]
    [InlineData("111.111.111-11")] // all-same is invalid
    [InlineData("12345678900")]    // wrong check digits
    [InlineData("123")]            // wrong length
    [InlineData("")]
    [InlineData(null)]
    public void IsValidCpf_False_For_Invalid(string? cpf) => Assert.False(DocumentValidator.IsValidCpf(cpf));

    [Theory]
    [InlineData("11.222.333/0001-81")]   // valid numeric CNPJ (formatted)
    [InlineData("11222333000181")]       // valid numeric CNPJ (digits only)
    [InlineData("12.ABC.345/01DE-35")]   // valid alphanumeric CNPJ (RFB official example, formatted)
    [InlineData("12ABC34501DE35")]       // valid alphanumeric CNPJ (raw)
    [InlineData("12abc34501de35")]       // lowercase is accepted (normalized to uppercase)
    public void IsValidCnpj_True_For_Valid(string cnpj) => Assert.True(DocumentValidator.IsValidCnpj(cnpj));

    [Theory]
    [InlineData("11.111.111/1111-11")]
    [InlineData("11222333000100")]  // wrong check digits (numeric)
    [InlineData("12ABC34501DE34")]  // wrong check digits (alphanumeric)
    [InlineData("12ABC34501DE3X")]  // non-numeric check digit
    [InlineData("123")]
    [InlineData(null)]
    public void IsValidCnpj_False_For_Invalid(string? cnpj) => Assert.False(DocumentValidator.IsValidCnpj(cnpj));

    [Theory]
    [InlineData("529.982.247-25", "52998224725")]
    [InlineData("11.222.333/0001-81", "11222333000181")]
    public void Normalize_Strips_NonDigits(string input, string expected) =>
        Assert.Equal(expected, DocumentValidator.Normalize(input));

    [Theory]
    [InlineData("11.222.333/0001-81", "11222333000181")]
    [InlineData("12.ABC.345/01DE-35", "12ABC34501DE35")]
    [InlineData("12abc34501de35", "12ABC34501DE35")]   // uppercased, letters kept
    public void NormalizeCnpj_Keeps_Alphanumerics_Uppercased(string input, string expected) =>
        Assert.Equal(expected, DocumentValidator.NormalizeCnpj(input));
}
