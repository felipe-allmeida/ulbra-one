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
    [InlineData("11.222.333/0001-81")] // valid CNPJ (formatted)
    [InlineData("11222333000181")]     // valid CNPJ (digits only)
    public void IsValidCnpj_True_For_Valid(string cnpj) => Assert.True(DocumentValidator.IsValidCnpj(cnpj));

    [Theory]
    [InlineData("11.111.111/1111-11")]
    [InlineData("11222333000100")] // wrong check digits
    [InlineData("123")]
    [InlineData(null)]
    public void IsValidCnpj_False_For_Invalid(string? cnpj) => Assert.False(DocumentValidator.IsValidCnpj(cnpj));

    [Theory]
    [InlineData("529.982.247-25", "52998224725")]
    [InlineData("11.222.333/0001-81", "11222333000181")]
    public void Normalize_Strips_NonDigits(string input, string expected) =>
        Assert.Equal(expected, DocumentValidator.Normalize(input));
}
