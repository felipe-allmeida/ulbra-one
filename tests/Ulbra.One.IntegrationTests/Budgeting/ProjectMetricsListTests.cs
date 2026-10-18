using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ulbra.One.Identity.Domain;
using Ulbra.One.IntegrationTests.Finance;
using Xunit;

namespace Ulbra.One.IntegrationTests.Budgeting;

/// <summary>GET /projects/metrics?ids=… — batch of GetProjectMetrics: one metrics row per id, keyed by the
/// prefixed project id. Non-existent/invalid/duplicate ids are dropped; empty → empty dict.</summary>
[Collection("Postgres")]
public class ProjectMetricsListTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> Ok(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static async Task<string> CreateProjectAsync(HttpClient admin, string unitId, string name, decimal budgetLimit)
    {
        var created = await Ok(await admin.PostAsJsonAsync("/api/projects", new
        {
            unitId,
            name,
            description = (string?)null,
            budgetLimit,
            initialConsumedValue = 0m,
        }));
        return created.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Returns_One_Metrics_Per_Id_Keyed_By_Prefixed_Id_Dropping_Unknown_And_Duplicate()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync($"boss_{Guid.NewGuid():N}@ulbra.br");
        var companyId = await FinanceSeed.CreateCompanyAsync(admin);
        var unitId = await FinanceSeed.CreateUnitAsync(admin, companyId, "Campus Teste", "CT");
        var a = await CreateProjectAsync(admin, unitId, $"Projeto A {Guid.NewGuid():N}", 500_000m);
        var b = await CreateProjectAsync(admin, unitId, $"Projeto B {Guid.NewGuid():N}", 800_000m);
        var missing = $"proj_{Guid.NewGuid():N}";

        var metrics = (await Ok(await admin.GetAsync(
                $"/api/projects/metrics?ids={a}&ids={b}&ids={a}&ids={missing}&ids=not-an-id")))
            .GetProperty("metrics");

        Assert.True(metrics.TryGetProperty(a, out _));
        Assert.True(metrics.TryGetProperty(b, out _));
        Assert.False(metrics.TryGetProperty(missing, out _));
        Assert.Equal(2, metrics.EnumerateObject().Count());
        Assert.Equal(500_000m, metrics.GetProperty(a).GetProperty("budgetLimit").GetDecimal());
    }

    [Fact]
    public async Task Empty_Ids_Returns_Empty_Dictionary()
    {
        var admin = await fixture.Api.CreateAdminClientAsync($"boss_{Guid.NewGuid():N}@ulbra.br");
        var metrics = (await Ok(await admin.GetAsync("/api/projects/metrics"))).GetProperty("metrics");
        Assert.Empty(metrics.EnumerateObject());
    }

    [Fact]
    public async Task Matches_The_Single_Endpoint()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync($"boss_{Guid.NewGuid():N}@ulbra.br");
        var companyId = await FinanceSeed.CreateCompanyAsync(admin);
        var unitId = await FinanceSeed.CreateUnitAsync(admin, companyId, "Campus Teste", "CT");
        var id = await CreateProjectAsync(admin, unitId, $"Projeto {Guid.NewGuid():N}", 120_000m);

        var batch = (await Ok(await admin.GetAsync($"/api/projects/metrics?ids={id}"))).GetProperty("metrics").GetProperty(id);
        var single = await Ok(await admin.GetAsync($"/api/projects/{id}/metrics"));

        Assert.Equal(single.GetProperty("budgetLimit").GetDecimal(), batch.GetProperty("budgetLimit").GetDecimal());
        Assert.Equal(single.GetProperty("previsto").GetDecimal(), batch.GetProperty("previsto").GetDecimal());
        Assert.Equal(single.GetProperty("realizado").GetDecimal(), batch.GetProperty("realizado").GetDecimal());
        Assert.Equal(single.GetProperty("saldo").GetDecimal(), batch.GetProperty("saldo").GetDecimal());
        Assert.Equal(single.GetProperty("isOverCeiling").GetBoolean(), batch.GetProperty("isOverCeiling").GetBoolean());
        Assert.Equal(single.GetProperty("requisitionCount").GetInt32(), batch.GetProperty("requisitionCount").GetInt32());
    }

    [Fact]
    public async Task Requires_ProjectsRead_Permission()
    {
        var reader = fixture.Api.CreateClientWith(Roles.Finance); // no permissions
        var res = await reader.GetAsync($"/api/projects/metrics?ids=proj_{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Anonymous_Returns_401()
    {
        var res = await fixture.Api.CreateClient().GetAsync($"/api/projects/metrics?ids=proj_{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
