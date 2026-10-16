using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ulbra.One.IntegrationTests.Finance;
using Xunit;

namespace Ulbra.One.IntegrationTests.Budgeting;

[Collection("Postgres")]
public class ProjectEndpointsTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    // Project names are unique per company, so each test seeds its own company and a random name —
    // on the shared DB a test only ever collides with its own duplicate insert.
    private static string UniqueName() => $"Project {Random.Shared.Next(100_000, 999_999)}";

    private static async Task<(string CompanyId, string UnitId, string CostCenterId, string VendorId)> SeedDimensionAsync(HttpClient client)
    {
        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var unitId = await FinanceSeed.CreateUnitAsync(client, companyId, "Campus Teste", "CT");
        // A unidade precisa estar associada ao CC — o create de requisição exige cobertura (AEL-830).
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, $"CC{Random.Shared.Next(100_000, 999_999)}", "Obras", unitId);
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Teste");
        return (companyId, unitId, costCenterId, vendorId);
    }

    private static object NewProject(string unitId, string name, decimal budgetLimit = 100_000m, decimal initialConsumed = 0m) =>
        new { unitId, name, description = "Obra do bloco A", budgetLimit, initialConsumedValue = initialConsumed };

    [Fact]
    public async Task Anonymous_Returns_401()
    {
        var factory = fixture.Api;
        var client = factory.CreateClient();
        var res = await client.GetAsync("/api/projects");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Create_Get_List_Update_Delete_Flow()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var (companyId, unitId, _, _) = await SeedDimensionAsync(client);
        var name = UniqueName();

        var createRes = await client.PostAsJsonAsync("/api/projects", NewProject(unitId, name, 250_000m));
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = await ReadJson(createRes);
        var id = created.GetProperty("id").GetString()!;
        Assert.Equal(companyId, created.GetProperty("companyId").GetString()); // derived from the unit
        Assert.Equal(name, created.GetProperty("name").GetString());
        Assert.Equal(250_000m, created.GetProperty("budgetLimit").GetDecimal());
        Assert.Equal("Active", created.GetProperty("status").GetString());

        var getRes = await client.GetAsync($"/api/projects/{id}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);

        var list = await ReadJson(await client.GetAsync($"/api/projects?unitId={unitId}"));
        var ids = list.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetString()).ToList();
        Assert.Contains(id, ids);

        var updateRes = await client.PutAsJsonAsync($"/api/projects/{id}",
            new { name, description = "atualizado", budgetLimit = 300_000m, initialConsumedValue = 1_000m });
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);
        var updated = await ReadJson(updateRes);
        Assert.Equal(300_000m, updated.GetProperty("budgetLimit").GetDecimal());
        Assert.Equal(1_000m, updated.GetProperty("initialConsumedValue").GetDecimal());

        var deleteRes = await client.DeleteAsync($"/api/projects/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteRes.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/projects/{id}")).StatusCode);
    }

    [Fact]
    public async Task Duplicate_Name_In_Same_Company_Returns_409()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var (_, unitId, _, _) = await SeedDimensionAsync(client);
        var name = UniqueName();

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/projects", NewProject(unitId, name))).StatusCode);
        var second = await client.PostAsJsonAsync("/api/projects", NewProject(unitId, name));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Negative_Budget_Limit_Returns_400()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var (_, unitId, _, _) = await SeedDimensionAsync(client);

        var res = await client.PostAsJsonAsync("/api/projects", NewProject(unitId, UniqueName(), budgetLimit: -1m));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Metrics_Reflect_Linked_Requisitions_And_Initial_Consumption()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionAsync(client);

        // Ceiling 100k with 10k already spent before the system went live.
        var project = await ReadJson(await client.PostAsJsonAsync("/api/projects",
            NewProject(unitId, UniqueName(), budgetLimit: 100_000m, initialConsumed: 10_000m)));
        var projectId = project.GetProperty("id").GetString()!;

        // A full-estimate requisition of 30k linked to the project.
        var reqRes = await client.PostAsJsonAsync("/api/requisitions", new
        {
            unitId,
            costCenterId,
            vendorId,
            projectId,
            type = "Purchase",
            description = "Compra de materiais",
            estimateValue = 30_000m,
            estimateType = "Full",
            startCompetency = "2026-07",
            firstDueDate = "2026-07-10",
        });
        Assert.Equal(HttpStatusCode.Created, reqRes.StatusCode);
        Assert.Equal(projectId, (await ReadJson(reqRes)).GetProperty("projectId").GetString());

        var metrics = await ReadJson(await client.GetAsync($"/api/projects/{projectId}/metrics"));
        Assert.Equal(100_000m, metrics.GetProperty("budgetLimit").GetDecimal());
        Assert.Equal(40_000m, metrics.GetProperty("previsto").GetDecimal());   // 30k pipeline + 10k initial (projection)
        Assert.Equal(10_000m, metrics.GetProperty("realizado").GetDecimal());  // só o consumo inicial
        // The 30k requisition is in pipeline (Draft) — it has not reached payment, so it does NOT reduce the real
        // saldo; only the initial consumption (10k) does. Saldo = 100k - 10k (AEL-844).
        Assert.Equal(90_000m, metrics.GetProperty("saldo").GetDecimal());
        Assert.False(metrics.GetProperty("isOverCeiling").GetBoolean());
        Assert.Equal(1, metrics.GetProperty("requisitionCount").GetInt32());
    }

    [Fact]
    public async Task Pipeline_Over_Ceiling_Does_Not_Flag_Overrun_Until_Payment()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionAsync(client);

        var project = await ReadJson(await client.PostAsJsonAsync("/api/projects",
            NewProject(unitId, UniqueName(), budgetLimit: 10_000m)));
        var projectId = project.GetProperty("id").GetString()!;

        await client.PostAsJsonAsync("/api/requisitions", new
        {
            unitId,
            costCenterId,
            vendorId,
            projectId,
            type = "Purchase",
            description = "Estoura o teto (mas ainda em pipeline)",
            estimateValue = 12_000m,
            estimateType = "Full",
            startCompetency = "2026-07",
            firstDueDate = "2026-07-10",
        });

        // The pipeline requisition (Draft) projects an overrun in the forecast, but does NOT reduce the real
        // saldo nor trigger the overrun until it reaches payment (AEL-844).
        var metrics = await ReadJson(await client.GetAsync($"/api/projects/{projectId}/metrics"));
        Assert.Equal(12_000m, metrics.GetProperty("previsto").GetDecimal());
        Assert.Equal(10_000m, metrics.GetProperty("saldo").GetDecimal());
        Assert.False(metrics.GetProperty("isOverCeiling").GetBoolean());
    }

    [Fact]
    public async Task Delete_With_Linked_Requisitions_Returns_409()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionAsync(client);

        var project = await ReadJson(await client.PostAsJsonAsync("/api/projects", NewProject(unitId, UniqueName())));
        var projectId = project.GetProperty("id").GetString()!;

        await client.PostAsJsonAsync("/api/requisitions", new
        {
            unitId,
            costCenterId,
            vendorId,
            projectId,
            type = "Purchase",
            description = "Vinculada",
            estimateValue = 1_000m,
            estimateType = "Full",
            startCompetency = "2026-07",
            firstDueDate = "2026-07-10",
        });

        var res = await client.DeleteAsync($"/api/projects/{projectId}");
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Requisition_Linked_To_Unknown_Project_Returns_400()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionAsync(client);

        var res = await client.PostAsJsonAsync("/api/requisitions", new
        {
            unitId,
            costCenterId,
            vendorId,
            projectId = $"project_{Guid.NewGuid():N}",
            type = "Purchase",
            description = "Project inexistente",
            estimateValue = 1_000m,
            estimateType = "Full",
            startCompetency = "2026-07",
            firstDueDate = "2026-07-10",
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
