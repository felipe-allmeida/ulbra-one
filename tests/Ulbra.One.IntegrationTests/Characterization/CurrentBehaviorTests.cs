using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ulbra.One.IntegrationTests.Characterization;

[Collection("Postgres")]
public class CurrentBehaviorTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Auth_Me_Requires_Token()
    {
        await using var f = new ApiFactory(fixture.ConnectionString);
        var anon = f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/auth/me")).StatusCode);

        var admin = await f.CreateAdminClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Full_Chain_Empresa_Unidade_Centro_Lancamento()
    {
        await using var f = new ApiFactory(fixture.ConnectionString);
        var client = await f.CreateAdminClientAsync();

        // Empresa
        var empRes = await client.PostAsJsonAsync("/api/empresas", new { razaoSocial = "ACME SA", cnpj = "11222333000181" });
        Assert.Equal(HttpStatusCode.Created, empRes.StatusCode);
        var empresaId = (await ReadJson(empRes)).GetProperty("id").GetGuid();

        // Unidade
        var uniRes = await client.PostAsJsonAsync("/api/unidades", new { empresaId, nome = "Campus Centro", sigla = "CC" });
        Assert.Equal(HttpStatusCode.Created, uniRes.StatusCode);
        var unidadeId = (await ReadJson(uniRes)).GetProperty("id").GetGuid();

        // Centro de custo with auto-approval
        var ccRes = await client.PostAsJsonAsync("/api/centros-custo", new
        {
            empresaId, codigo = "MKT", nome = "Marketing",
            orcamentoAnual = 100000m, limiteAutoAprovacao = 5000m, unidadeIds = new[] { unidadeId }
        });
        Assert.Equal(HttpStatusCode.Created, ccRes.StatusCode);
        var centroId = (await ReadJson(ccRes)).GetProperty("id").GetGuid();

        // Lancamento under the auto-approval limit -> should be auto-approved
        var lanRes = await client.PostAsJsonAsync("/api/lancamentos", new
        {
            empresaId, unidadeId, centroDeCustoId = centroId, tipo = "Pagar",
            descricao = "Anúncios", contraparte = "Google", valor = 3000m, vencimento = "2026-07-01"
        });
        Assert.Equal(HttpStatusCode.Created, lanRes.StatusCode);
        var lan = await ReadJson(lanRes);
        Assert.Equal("Aprovado", lan.GetProperty("status").GetString());

        // List by empresa returns it
        var list = await ReadJson(await client.GetAsync($"/api/lancamentos?empresaId={empresaId}"));
        Assert.True(list.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Lancamento_Above_Limit_Stays_Pendente_Then_Approve_Then_Pay()
    {
        await using var f = new ApiFactory(fixture.ConnectionString);
        var client = await f.CreateAdminClientAsync();

        var empresaId = (await ReadJson(await client.PostAsJsonAsync("/api/empresas",
            new { razaoSocial = "Beta SA", cnpj = "11444777000161" }))).GetProperty("id").GetGuid();
        var unidadeId = (await ReadJson(await client.PostAsJsonAsync("/api/unidades",
            new { empresaId, nome = "Sede", sigla = "SE" }))).GetProperty("id").GetGuid();
        var centroId = (await ReadJson(await client.PostAsJsonAsync("/api/centros-custo",
            new { empresaId, codigo = "TI", nome = "TI", orcamentoAnual = 50000m, limiteAutoAprovacao = 0m, unidadeIds = new[] { unidadeId } })))
            .GetProperty("id").GetGuid();

        var lanId = (await ReadJson(await client.PostAsJsonAsync("/api/lancamentos", new
        {
            empresaId, unidadeId, centroDeCustoId = centroId, tipo = "Pagar",
            descricao = "Servidor", contraparte = "AWS", valor = 20000m, vencimento = "2026-08-01"
        }))).GetProperty("id").GetGuid();

        Assert.Equal("Pendente", (await ReadJson(await client.GetAsync($"/api/lancamentos/{lanId}"))).GetProperty("status").GetString());

        await client.PostAsync($"/api/lancamentos/{lanId}/aprovar", null);
        Assert.Equal("Aprovado", (await ReadJson(await client.GetAsync($"/api/lancamentos/{lanId}"))).GetProperty("status").GetString());

        await client.PostAsync($"/api/lancamentos/{lanId}/pagar", null);
        Assert.Equal("Pago", (await ReadJson(await client.GetAsync($"/api/lancamentos/{lanId}"))).GetProperty("status").GetString());
    }
}
