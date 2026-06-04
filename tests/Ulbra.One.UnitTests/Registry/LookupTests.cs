using System.Net;
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
        var lookup = new BrasilApiCnpjLookup(Client(HttpStatusCode.OK, json));
        var result = await lookup.FindAsync("11222333000181", CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("Empresa X LTDA", result!.RazaoSocial);
        Assert.Equal("Porto Alegre", result.Address.City);
    }

    [Fact]
    public async Task Cnpj_Returns_Null_On_Http_Error()
    {
        var lookup = new BrasilApiCnpjLookup(Client(HttpStatusCode.NotFound, ""));
        Assert.Null(await lookup.FindAsync("00000000000000", CancellationToken.None));
    }
}
