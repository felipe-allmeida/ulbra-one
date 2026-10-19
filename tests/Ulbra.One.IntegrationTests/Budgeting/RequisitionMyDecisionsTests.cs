using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Identity.Domain;
using Ulbra.One.IntegrationTests.Finance;
using Xunit;

namespace Ulbra.One.IntegrationTests.Budgeting;

/// <summary>GET /requisitions/my-decisions (AEL-859): the approver's personal decision log — one row per
/// Approve/ReturnForComplement/Cancel transition of the CALLING user, newest first, with the vendor name
/// resolved server-side.</summary>
[Collection("Postgres")]
public class RequisitionMyDecisionsTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> Ok(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static async Task<(string CompanyId, string UnitId, string CostCenterId, string Id)> DriveToApprovalAsync(HttpClient admin)
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
            monthlyLimit = 1_000_000m,
        }));
        var vendorId = await FinanceSeed.CreateVendorAsync(admin, "Fornecedor Teste");
        var created = await Ok(await admin.PostAsJsonAsync("/api/requisitions", new
        {
            unitId,
            costCenterId,
            vendorId,
            type = "Purchase",
            description = "Comprar cadeiras",
            observation = (string?)null,
            estimateValue = 1_000m,
            estimateType = "Full",
            startCompetency = "2026-07",
            endCompetency = (string?)null,
            firstDueDate = "2026-07-10",
            paymentFrequency = (string?)null,
            paymentInstallmentsCount = (int?)null,
        }));
        var id = created.GetProperty("id").GetString()!;
        await Ok(await admin.PostAsJsonAsync($"/api/requisitions/{id}/submit", new { }));
        await Ok(await admin.PostAsJsonAsync($"/api/requisitions/{id}/approve-triage", new { }));
        await Ok(await admin.PostAsJsonAsync($"/api/requisitions/{id}/submit-financial-complement", new
        {
            issueDate = "2026-07-05",
            installments = new[] { new { dueDate = "2026-07-10", amount = 1_000m, paymentMethod = "Pix" } },
        }));
        return (companyId, unitId, costCenterId, id);
    }

    [Fact]
    public async Task Lists_Only_Callers_Decisions_Newest_First_With_Vendor_Name()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync($"boss_{Guid.NewGuid():N}@ulbra.br");
        // Approve, Return AND Cancel all read the Approver's scope LIVE from the DB (AEL-883 intersection), so
        // each approver is a persisted user whose scope must cover every requisition it acts on. Each drive
        // creates its own company/unit/cost center, so seed a grant per acted-on requisition.
        var (approver, approverId) = await factory.CreatePersistedUserClientAsync(Roles.Approver,
            Permissions.RequisitionsRead, Permissions.RequisitionsApprove);
        var (otherApprover, otherApproverId) = await factory.CreatePersistedUserClientAsync(Roles.Approver,
            Permissions.RequisitionsRead, Permissions.RequisitionsApprove);

        var (companyId, unitId, costCenterId, approvedId) = await DriveToApprovalAsync(admin);
        var (rCompanyId, rUnitId, rCostCenterId, returnedId) = await DriveToApprovalAsync(admin);
        var (cCompanyId, cUnitId, cCostCenterId, cancelledId) = await DriveToApprovalAsync(admin);
        var (fCompanyId, fUnitId, fCostCenterId, foreignId) = await DriveToApprovalAsync(admin);

        await factory.SeedUserScopeAsync(approverId,
            CompanyId.Parse(companyId, null).Value, UnitId.Parse(unitId, null).Value, CostCenterId.Parse(costCenterId, null).Value);
        await factory.SeedUserScopeAsync(approverId,
            CompanyId.Parse(rCompanyId, null).Value, UnitId.Parse(rUnitId, null).Value, CostCenterId.Parse(rCostCenterId, null).Value);
        await factory.SeedUserScopeAsync(approverId,
            CompanyId.Parse(cCompanyId, null).Value, UnitId.Parse(cUnitId, null).Value, CostCenterId.Parse(cCostCenterId, null).Value);
        await factory.SeedUserScopeAsync(otherApproverId,
            CompanyId.Parse(fCompanyId, null).Value, UnitId.Parse(fUnitId, null).Value, CostCenterId.Parse(fCostCenterId, null).Value);

        await Ok(await approver.PostAsJsonAsync($"/api/requisitions/{approvedId}/approve", new { }));
        await Ok(await approver.PostAsJsonAsync($"/api/requisitions/{returnedId}/return-for-complement", new { reason = "faltou cotação" }));
        await Ok(await approver.PostAsJsonAsync($"/api/requisitions/{cancelledId}/cancel", new { reason = "duplicada" }));
        // A decision by ANOTHER approver must not leak into the caller's log.
        await Ok(await otherApprover.PostAsJsonAsync($"/api/requisitions/{foreignId}/approve", new { }));

        var page = await Ok(await approver.GetAsync("/api/requisitions/my-decisions"));
        var items = page.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, items.Count);
        Assert.Equal(new[] { "Cancel", "ReturnForComplement", "Approve" },
            items.Select(i => i.GetProperty("decisionAction").GetString()).ToArray());
        Assert.Equal("duplicada", items[0].GetProperty("decisionReason").GetString());
        Assert.All(items, i => Assert.Equal("Fornecedor Teste", i.GetProperty("vendorName").GetString()));
        Assert.All(items, i => Assert.False(string.IsNullOrEmpty(i.GetProperty("decidedAt").GetString())));

        // companyId filter: the approved decision belongs to `companyId`; an unrelated company yields nothing.
        var filtered = await Ok(await approver.GetAsync($"/api/requisitions/my-decisions?companyId={companyId}"));
        Assert.Single(filtered.GetProperty("items").EnumerateArray());
        var none = await Ok(await approver.GetAsync($"/api/requisitions/my-decisions?companyId=company_{Guid.NewGuid():N}"));
        Assert.Empty(none.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Requires_Approve_Permission()
    {
        var factory = fixture.Api;
        var reader = factory.CreateClientWith(Roles.Finance, Permissions.RequisitionsRead);
        var res = await reader.GetAsync("/api/requisitions/my-decisions");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Anonymous_Returns_401()
    {
        var res = await fixture.Api.CreateClient().GetAsync("/api/requisitions/my-decisions");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
