using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Ulbra.One.Registry.Application.Lookups;
using Ulbra.One.Registry.Domain.Entities;
using Ulbra.One.Registry.Infrastructure.Lookups;

namespace Ulbra.One.UnitTests.Registry;

public class LookupTests
{
    private sealed class StubHandler(HttpStatusCode code, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });
    }

    private static HttpClient Client(HttpStatusCode code, string body) =>
        new(new StubHandler(code, body)) { BaseAddress = new Uri("https://stub.local") };

    private static BrasilApiCnpjLookup CnpjLookup(HttpStatusCode code, string body) =>
        new(Client(code, body), NullLogger<BrasilApiCnpjLookup>.Instance);

    [Fact]
    public async Task ViaCep_Maps_Address_Fields()
    {
        var json = """{"cep":"01001-000","logradouro":"Praça da Sé","bairro":"Sé","localidade":"São Paulo","uf":"SP"}""";
        var lookup = new ViaCepLookup(Client(HttpStatusCode.OK, json));
        var result = await lookup.FindAsync("01001000", CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("Praça da Sé", result!.Street);
        Assert.Equal("São Paulo", result.City);
        Assert.Equal("SP", result.State);
    }

    [Fact]
    public async Task ViaCep_Returns_Null_When_NotFound_Payload()
    {
        var lookup = new ViaCepLookup(Client(HttpStatusCode.OK, """{"erro":true}"""));
        Assert.Null(await lookup.FindAsync("00000000", CancellationToken.None));
    }

    [Fact]
    public async Task Cnpj_Maps_Company_Fields()
    {
        var json = """{"razao_social":"Empresa X LTDA","nome_fantasia":"X","logradouro":"Rua A","numero":"10","bairro":"Centro","municipio":"Porto Alegre","uf":"RS","cep":"90000000"}""";
        var result = await CnpjLookup(HttpStatusCode.OK, json).FindAsync("11222333000181", CancellationToken.None);
        Assert.Equal(CnpjLookupStatus.Found, result.Status);
        Assert.Equal("Empresa X LTDA", result.Value!.RazaoSocial);
        Assert.Equal("Porto Alegre", result.Value.Address.City);
    }

    [Fact]
    public async Task Cnpj_Maps_TaxRegime_From_Latest_RegimeTributario_And_Cnae()
    {
        var json = """{"razao_social":"Empresa X LTDA","cnae_fiscal":6201500,"cnae_fiscal_descricao":"Desenvolvimento de software","regime_tributario":[{"ano":2022,"forma_de_tributacao":"LUCRO REAL"},{"ano":2024,"forma_de_tributacao":"LUCRO PRESUMIDO"}]}""";
        var result = await CnpjLookup(HttpStatusCode.OK, json).FindAsync("11222333000181", CancellationToken.None);
        Assert.Equal(TaxRegime.LucroPresumido, result.Value!.SuggestedTaxRegime); // most recent year wins
        Assert.Equal("6201500", result.Value.PrimaryCnaeCode);
        Assert.Equal("Desenvolvimento de software", result.Value.PrimaryCnaeDescription);
    }

    [Fact]
    public async Task Cnpj_Maps_Simples_And_Mei_From_Opcao_Flags()
    {
        var simples = await CnpjLookup(HttpStatusCode.OK, """{"razao_social":"S","opcao_pelo_simples":true}""")
            .FindAsync("11222333000181", CancellationToken.None);
        Assert.Equal(TaxRegime.SimplesNacional, simples.Value!.SuggestedTaxRegime);

        var mei = await CnpjLookup(HttpStatusCode.OK, """{"razao_social":"M","opcao_pelo_mei":true,"opcao_pelo_simples":true}""")
            .FindAsync("11222333000181", CancellationToken.None);
        Assert.Equal(TaxRegime.Mei, mei.Value!.SuggestedTaxRegime); // MEI wins over Simples
    }

    [Fact]
    public async Task Cnpj_TaxRegime_Null_When_Immune_Or_Absent()
    {
        var immune = await CnpjLookup(HttpStatusCode.OK, """{"razao_social":"A","regime_tributario":[{"ano":2024,"forma_de_tributacao":"IMUNE DE IRPJ"}]}""")
            .FindAsync("11222333000181", CancellationToken.None);
        Assert.Null(immune.Value!.SuggestedTaxRegime);

        var none = await CnpjLookup(HttpStatusCode.OK, """{"razao_social":"A"}""")
            .FindAsync("11222333000181", CancellationToken.None);
        Assert.Null(none.Value!.SuggestedTaxRegime);
        Assert.Null(none.Value.PrimaryCnaeCode);
    }

    [Fact]
    public async Task Cnpj_Returns_NotFound_On_404()
    {
        var result = await CnpjLookup(HttpStatusCode.NotFound, "").FindAsync("11222333000181", CancellationToken.None);
        Assert.Equal(CnpjLookupStatus.NotFound, result.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]        // missing/blocked User-Agent (the real bug)
    [InlineData(HttpStatusCode.TooManyRequests)]  // rate limit
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Cnpj_Returns_Unavailable_On_Upstream_Failure(HttpStatusCode code)
    {
        var result = await CnpjLookup(code, "").FindAsync("11222333000181", CancellationToken.None);
        Assert.Equal(CnpjLookupStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task Cnpj_Returns_NotFound_When_Malformed()
    {
        var result = await CnpjLookup(HttpStatusCode.OK, "").FindAsync("123", CancellationToken.None);
        Assert.Equal(CnpjLookupStatus.NotFound, result.Status);
    }
}
