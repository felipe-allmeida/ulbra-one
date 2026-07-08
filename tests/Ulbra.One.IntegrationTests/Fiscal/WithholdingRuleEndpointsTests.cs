using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ulbra.One.IntegrationTests.Fiscal;

/// <summary>
/// Integration tests for the WithholdingRule CRUD endpoints (/api/withholding-rules), focused on the INSS
/// dimensions Cprb / SimplesAnexo surviving Create → List → Update. Regression guard: the Update path used to
/// drop those two fields, so a rule edited via the API silently lost its CPRB / Anexo pinning.
/// </summary>
[Collection("Postgres")]
public class WithholdingRuleEndpointsTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    /// <summary>Fetches the rule by id from the (shared-DB) list; asserts exactly one match.</summary>
    private static async Task<JsonElement> FindRuleAsync(HttpClient client, string id)
    {
        var list = await ReadJson(await client.GetAsync("/api/withholding-rules?includeInactive=true"));
        var matches = list.EnumerateArray().Where(r => r.GetProperty("id").GetString() == id).ToList();
        Assert.Single(matches);
        return matches[0];
    }

    [Fact]
    public async Task Create_InssCprbRule_persists_cprb_through_list()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        var createRes = await client.PostAsJsonAsync("/api/withholding-rules", new
        {
            taxType = "Inss",
            retains = true,
            reason = "None",
            rateSource = "Fixed",
            rate = 0.035m,
            baseRule = "Full",
            cprb = true,
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = await ReadJson(createRes);
        Assert.True(created.GetProperty("cprb").GetBoolean());
        var id = created.GetProperty("id").GetString()!;

        var rule = await FindRuleAsync(client, id);
        Assert.Equal("Inss", rule.GetProperty("taxType").GetString());
        Assert.True(rule.GetProperty("cprb").GetBoolean());
        Assert.Equal(0.035m, rule.GetProperty("rate").GetDecimal());
        Assert.Equal(JsonValueKind.Null, rule.GetProperty("simplesAnexo").ValueKind);
    }

    [Fact]
    public async Task Create_SimplesAnexoIvRule_persists_anexo_through_list()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        var createRes = await client.PostAsJsonAsync("/api/withholding-rules", new
        {
            taxType = "Inss",
            retains = true,
            reason = "None",
            rateSource = "Fixed",
            rate = 0.11m,
            baseRule = "Full",
            vendorRegime = "SimplesNacional",
            simplesAnexo = "IV",
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var id = (await ReadJson(createRes)).GetProperty("id").GetString()!;

        var rule = await FindRuleAsync(client, id);
        Assert.Equal("SimplesNacional", rule.GetProperty("vendorRegime").GetString());
        Assert.Equal("IV", rule.GetProperty("simplesAnexo").GetString());
    }

    [Fact]
    public async Task Update_changes_cprb_reflected_in_list()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        // Create a plain INSS rule (no INSS dimensions).
        var createRes = await client.PostAsJsonAsync("/api/withholding-rules", new
        {
            taxType = "Inss", retains = true, reason = "None", rateSource = "Fixed", rate = 0.11m, baseRule = "Full",
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var id = (await ReadJson(createRes)).GetProperty("id").GetString()!;

        // Update to pin CPRB 3,5% — the field must round-trip (regression: Update dropped it).
        var updateRes = await client.PutAsJsonAsync($"/api/withholding-rules/{id}", new
        {
            taxType = "Inss", retains = true, reason = "None", rateSource = "Fixed", rate = 0.035m, baseRule = "Full",
            cprb = true,
        });
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);

        var rule = await FindRuleAsync(client, id);
        Assert.True(rule.GetProperty("cprb").GetBoolean());
        Assert.Equal(0.035m, rule.GetProperty("rate").GetDecimal());
    }
}
