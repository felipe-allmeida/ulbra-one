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
    public async Task Full_Chain_Company_Unit_CostCenter_Transaction()
    {
        await using var f = new ApiFactory(fixture.ConnectionString);
        var client = await f.CreateAdminClientAsync();

        // Company
        var empRes = await client.PostAsJsonAsync("/api/companies", new { legalName = "ACME SA", cnpj = "11222333000181" });
        Assert.Equal(HttpStatusCode.Created, empRes.StatusCode);
        var companyId = (await ReadJson(empRes)).GetProperty("id").GetString();

        // Unit
        var uniRes = await client.PostAsJsonAsync("/api/units", new { companyId, name = "Campus Centro", acronym = "CC" });
        Assert.Equal(HttpStatusCode.Created, uniRes.StatusCode);
        var unitId = (await ReadJson(uniRes)).GetProperty("id").GetString();

        // Cost center with auto-approval
        var ccRes = await client.PostAsJsonAsync("/api/cost-centers", new
        {
            companyId, code = "MKT", name = "Marketing",
            annualBudget = 100000m, autoApprovalLimit = 5000m, unitIds = new[] { unitId }
        });
        Assert.Equal(HttpStatusCode.Created, ccRes.StatusCode);
        var centroId = (await ReadJson(ccRes)).GetProperty("id").GetString();

        // Transaction under the auto-approval limit -> should be auto-approved
        var lanRes = await client.PostAsJsonAsync("/api/transactions", new
        {
            companyId, unitId, costCenterId = centroId, type = "Payable",
            description = "Anúncios", counterparty = "Google", amount = 3000m, dueDate = "2026-07-01"
        });
        Assert.Equal(HttpStatusCode.Created, lanRes.StatusCode);
        var lan = await ReadJson(lanRes);
        Assert.Equal("Approved", lan.GetProperty("status").GetString());

        // List by company returns it (paginated envelope)
        var list = await ReadJson(await client.GetAsync($"/api/transactions?companyId={companyId}"));
        Assert.True(list.GetProperty("items").GetArrayLength() >= 1);
        Assert.True(list.GetProperty("total").GetInt32() >= 1);
        Assert.Equal(1, list.GetProperty("page").GetInt32());
    }

    [Fact]
    public async Task Transaction_Above_Limit_Stays_Pending_Then_Approve_Then_Pay()
    {
        await using var f = new ApiFactory(fixture.ConnectionString);
        var client = await f.CreateAdminClientAsync();

        var companyId = (await ReadJson(await client.PostAsJsonAsync("/api/companies",
            new { legalName = "Beta SA", cnpj = "11444777000161" }))).GetProperty("id").GetString();
        var unitId = (await ReadJson(await client.PostAsJsonAsync("/api/units",
            new { companyId, name = "Sede", acronym = "SE" }))).GetProperty("id").GetString();
        var centroId = (await ReadJson(await client.PostAsJsonAsync("/api/cost-centers",
            new { companyId, code = "TI", name = "TI", annualBudget = 50000m, autoApprovalLimit = 0m, unitIds = new[] { unitId } })))
            .GetProperty("id").GetString();

        var lanId = (await ReadJson(await client.PostAsJsonAsync("/api/transactions", new
        {
            companyId, unitId, costCenterId = centroId, type = "Payable",
            description = "Servidor", counterparty = "AWS", amount = 20000m, dueDate = "2026-08-01"
        }))).GetProperty("id").GetString();

        Assert.Equal("Pending", (await ReadJson(await client.GetAsync($"/api/transactions/{lanId}"))).GetProperty("status").GetString());

        await client.PostAsync($"/api/transactions/{lanId}/approve", null);
        Assert.Equal("Approved", (await ReadJson(await client.GetAsync($"/api/transactions/{lanId}"))).GetProperty("status").GetString());

        await client.PostAsync($"/api/transactions/{lanId}/pay", null);
        Assert.Equal("Paid", (await ReadJson(await client.GetAsync($"/api/transactions/{lanId}"))).GetProperty("status").GetString());
    }
}
