using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Identity.Domain;
using Ulbra.One.IntegrationTests.Finance;
using Xunit;

namespace Ulbra.One.IntegrationTests.Budgeting;

/// <summary>GET /requisitions/budget-controls?ids=… — batch of GetRequisitionBudgetControl: one verdict per id,
/// keyed by the prefixed requisition id. Non-existent/invalid/duplicate ids are dropped; empty → empty dict.</summary>
[Collection("Postgres")]
public class RequisitionBudgetControlsTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> Ok(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static async Task<(string CompanyId, string UnitId, string CostCenterId, string VendorId)> SeedDimensionsAsync(
        HttpClient admin, decimal monthlyLimit)
    {
        var companyId = await FinanceSeed.CreateCompanyAsync(admin);
        var unitId = await FinanceSeed.CreateUnitAsync(admin, companyId, "Campus Teste", "CT");
        var cc = await Ok(await admin.PostAsJsonAsync("/api/cost-centers", new
        {
            companyId,
            code = $"CC{Random.Shared.Next(100_000, 999_999)}",
            name = "Marketing",
            autoApprovalLimit = 0m,
            unitIds = new[] { unitId },
        }));
        var costCenterId = cc.GetProperty("id").GetString()!;
        await Ok(await admin.PostAsJsonAsync($"/api/cost-centers/{costCenterId}/unit-budgets", new
        {
            unitId,
            startCompetency = "2026-07",
            endCompetency = "2026-12",
            monthlyLimit,
        }));
        var vendorId = await FinanceSeed.CreateVendorAsync(admin, "Fornecedor Teste");
        return (companyId, unitId, costCenterId, vendorId);
    }

    private static async Task<string> CreateRequisitionAsync(
        HttpClient admin, string unitId, string costCenterId, string vendorId, decimal estimateValue)
    {
        var created = await Ok(await admin.PostAsJsonAsync("/api/requisitions", new
        {
            unitId,
            costCenterId,
            vendorId,
            type = "Purchase",
            description = "Compra",
            observation = (string?)null,
            estimateValue,
            estimateType = "Full",
            startCompetency = "2026-07",
            endCompetency = (string?)null,
            firstDueDate = "2026-07-10",
            paymentFrequency = (string?)null,
            paymentInstallmentsCount = (int?)null,
        }));
        return created.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Returns_One_Verdict_Per_Id_Keyed_By_Prefixed_Id_Dropping_Unknown_And_Duplicate()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync($"boss_{Guid.NewGuid():N}@ulbra.br");
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionsAsync(admin, 1_000_000m);
        var a = await CreateRequisitionAsync(admin, unitId, costCenterId, vendorId, 1_000m);
        var b = await CreateRequisitionAsync(admin, unitId, costCenterId, vendorId, 2_000m);
        var missing = $"req_{Guid.NewGuid():N}";

        // Valid a + b, a duplicate of a, one non-existent, and one unparseable id.
        var controls = (await Ok(await admin.GetAsync(
                $"/api/requisitions/budget-controls?ids={a}&ids={b}&ids={a}&ids={missing}&ids=not-an-id")))
            .GetProperty("controls");

        Assert.True(controls.TryGetProperty(a, out _));
        Assert.True(controls.TryGetProperty(b, out _));
        Assert.False(controls.TryGetProperty(missing, out _)); // non-existent omitted
        Assert.Equal(2, controls.EnumerateObject().Count()); // no dupes, no junk
        // Each entry is a full budget-control panel.
        Assert.True(controls.GetProperty(a).TryGetProperty("months", out _));
        Assert.True(controls.GetProperty(a).TryGetProperty("candidateExceeds", out _));
    }

    [Fact]
    public async Task Empty_Ids_Returns_Empty_Dictionary()
    {
        var admin = await fixture.Api.CreateAdminClientAsync($"boss_{Guid.NewGuid():N}@ulbra.br");
        var controls = (await Ok(await admin.GetAsync("/api/requisitions/budget-controls"))).GetProperty("controls");
        Assert.Empty(controls.EnumerateObject());
    }

    [Fact]
    public async Task Over_Budget_Requisition_Reports_Breach_And_Matches_The_Single_Endpoint()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync($"boss_{Guid.NewGuid():N}@ulbra.br");
        // Tiny ceiling, large estimate → the candidate breaches its competency.
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionsAsync(admin, 100m);
        var id = await CreateRequisitionAsync(admin, unitId, costCenterId, vendorId, 10_000m);

        var batch = (await Ok(await admin.GetAsync($"/api/requisitions/budget-controls?ids={id}")))
            .GetProperty("controls").GetProperty(id);
        var single = await Ok(await admin.GetAsync($"/api/requisitions/{id}/budget-control"));

        Assert.True(batch.GetProperty("candidateExceeds").GetBoolean());
        Assert.True(batch.GetProperty("breaches").GetArrayLength() > 0);
        // Numeric parity with the per-id endpoint (same From mapping).
        Assert.Equal(single.GetProperty("candidateExceeds").GetBoolean(), batch.GetProperty("candidateExceeds").GetBoolean());
        Assert.Equal(single.GetProperty("breaches").GetArrayLength(), batch.GetProperty("breaches").GetArrayLength());
        Assert.Equal(single.GetProperty("unbudgeted").GetArrayLength(), batch.GetProperty("unbudgeted").GetArrayLength());
        Assert.Equal(single.GetProperty("months").GetArrayLength(), batch.GetProperty("months").GetArrayLength());
    }

    [Fact]
    public async Task Requires_RequisitionsRead_Permission()
    {
        var reader = fixture.Api.CreateClientWith(Roles.Finance); // no permissions
        var res = await reader.GetAsync($"/api/requisitions/budget-controls?ids=req_{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Anonymous_Returns_401()
    {
        var res = await fixture.Api.CreateClient().GetAsync($"/api/requisitions/budget-controls?ids=req_{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
