using Ulbra.One.FirestoreImport.Mapping;
using Xunit;

namespace Ulbra.One.FirestoreImport.Tests;

public class DocumentNormalizerTests
{
    [Theory]
    [InlineData("123.456.789-09", "12345678909")]
    [InlineData(" 011.222.333-44 ", "01122233344")]
    [InlineData(null, "")]
    public void Normalize_keeps_digits_only(string? input, string expected)
        => Assert.Equal(expected, DocumentNormalizer.Normalize(input));

    [Theory]
    [InlineData("88.332.466/0002-94", "88332466000294")]
    [InlineData("12.abc.678/0001-90", "12ABC678000190")]
    public void NormalizeCnpj_keeps_alphanumeric_upper(string input, string expected)
        => Assert.Equal(expected, DocumentNormalizer.NormalizeCnpj(input));
}

public class VendorMappingTests
{
    [Fact]
    public void Cpf_maps_to_individual()
    {
        var r = VendorMapping.ResolveDocument("123.456.789-09");
        Assert.Equal(VendorMapping.PersonTypeIndividual, r.PersonType);
        Assert.Equal("12345678909", r.Document);
    }

    [Fact]
    public void Cnpj_maps_to_company()
    {
        var r = VendorMapping.ResolveDocument("88.332.466/0002-94");
        Assert.Equal(VendorMapping.PersonTypeCompany, r.PersonType);
        Assert.Equal("88332466000294", r.Document);
    }

    [Theory]
    [InlineData("Simples Nacional", "SimplesNacional")]
    [InlineData("MEI", "Mei")]
    [InlineData("Lucro Presumido", "LucroPresumido")]
    [InlineData("LUCRO REAL", "LucroReal")]
    [InlineData("", "Unknown")]
    [InlineData(null, "Unknown")]
    [InlineData("qualquer coisa", "Unknown")]
    public void TaxRegime_maps_by_keyword(string? input, string expected)
        => Assert.Equal(expected, VendorMapping.ResolveTaxRegime(input));
}

public class PixKeyNormalizerTests
{
    [Fact]
    public void Email_key_is_trimmed_only()
        => Assert.Equal("a@b.com", PixKeyNormalizer.Normalize("  a@b.com "));

    [Fact]
    public void Random_evp_key_is_kept_intact()
    {
        const string evp = "123e4567-e89b-12d3-a456-426614174000";
        Assert.Equal(evp, PixKeyNormalizer.Normalize(evp));
    }

    [Fact]
    public void Phone_key_becomes_digits()
        => Assert.Equal("5551999998888", PixKeyNormalizer.Normalize("+55 (51) 99999-8888"));

    [Fact]
    public void Empty_key_becomes_empty()
        => Assert.Equal("", PixKeyNormalizer.Normalize("   "));
}

public class NatureKeyTests
{
    [Fact]
    public void Uppercase_accented_matches_titlecase()
        => Assert.Equal(NatureKey.Of("Tecnologia e Inovação"), NatureKey.Of("TECNOLOGIA E INOVAÇÃO"));

    [Fact]
    public void Collapses_whitespace_and_strips_accents()
        => Assert.Equal("PRESIDENCIA", NatureKey.Of("  Presidência  "));

    [Fact]
    public void Null_becomes_empty()
        => Assert.Equal("", NatureKey.Of(null));
}

public class RoleMapTests
{
    [Theory]
    [InlineData("Administrador", "Admin")]
    [InlineData("Aprovador", "Approver")]
    [InlineData("Solicitante", "Requester")]
    [InlineData("Financeiro", "Finance")]
    [InlineData("Contabilidade", "Accounting")]
    [InlineData("Presidência", "Presidency")]
    [InlineData("Controle", "Controllership")]
    public void Known_roles_map_pt_to_en(string legacy, string expected)
    {
        Assert.Equal(expected, RoleMap.Resolve(legacy, out var matched));
        Assert.True(matched);
    }

    [Fact]
    public void Unknown_role_falls_back_to_user()
    {
        Assert.Equal("User", RoleMap.Resolve("Coordenador de Esportes", out var matched));
        Assert.False(matched);
    }

    [Fact]
    public void Best_picks_highest_privilege_across_role_and_roles()
    {
        var best = RoleMap.ResolveBest("Solicitante", ["Aprovador"], out var anyMatched);
        Assert.Equal("Approver", best);
        Assert.True(anyMatched);
    }

    [Fact]
    public void Best_with_no_known_labels_is_user_and_unmatched()
    {
        var best = RoleMap.ResolveBest("Desconhecido", [], out var anyMatched);
        Assert.Equal("User", best);
        Assert.False(anyMatched);
    }
}
