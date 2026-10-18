using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Identity.Domain;
using Ulbra.One.IntegrationTests.Finance;
using Xunit;

namespace Ulbra.One.IntegrationTests.Budgeting;

/// <summary>GET /requisitions/stage-counts: per-status counts + summed estimated value in one grouped query
/// (replaces the per-status list fan-out). Honours the same company / "mine" / permission scope as the list.</summary>
[Collection("Postgres")]
public class RequisitionStageCountsTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> Ok(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static async Task<(string CompanyId, string UnitId, string CostCenterId, string VendorId)> SeedDimensionsAsync(HttpClient admin)
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
            description = "Comprar cadeiras",
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
    public async Task Counts_And_Sums_By_Status_Scoped_To_Company()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync($"boss_{Guid.NewGuid():N}@ulbra.br");
        var (companyId, unitId, costCenterId, vendorId) = await SeedDimensionsAsync(admin);

        // One left in Draft, one moved to Triage, one driven to Approval (worth 1500 there).
        await CreateRequisitionAsync(admin, unitId, costCenterId, vendorId, 1_000m);

        var triageId = await CreateRequisitionAsync(admin, unitId, costCenterId, vendorId, 2_000m);
        await Ok(await admin.PostAsJsonAsync($"/api/requisitions/{triageId}/submit", new { }));

        var approvalId = await CreateRequisitionAsync(admin, unitId, costCenterId, vendorId, 1_500m);
        await Ok(await admin.PostAsJsonAsync($"/api/requisitions/{approvalId}/submit", new { }));
        await Ok(await admin.PostAsJsonAsync($"/api/requisitions/{approvalId}/approve-triage", new { }));
        await Ok(await admin.PostAsJsonAsync($"/api/requisitions/{approvalId}/submit-financial-complement", new
        {
            issueDate = "2026-07-05",
            installments = new[] { new { dueDate = "2026-07-10", amount = 1_500m, paymentMethod = "Pix" } },
        }));

        var stages = (await Ok(await admin.GetAsync($"/api/requisitions/stage-counts?companyId={companyId}")))
            .GetProperty("stages");

        Assert.Equal(1, stages.GetProperty("Draft").GetProperty("count").GetInt32());
        Assert.Equal(1, stages.GetProperty("Triage").GetProperty("count").GetInt32());
        Assert.Equal(1, stages.GetProperty("Approval").GetProperty("count").GetInt32());
        // Summed estimated value of the Approval stage (this company only).
        Assert.Equal(1_500m, stages.GetProperty("Approval").GetProperty("total").GetDecimal());
        // A stage with no requisitions is simply absent (the client defaults it to zero).
        Assert.False(stages.TryGetProperty("Paid", out _));
    }

    [Fact]
    public async Task Mine_Scopes_To_Caller()
    {
        var factory = fixture.Api;
        var owner = await factory.CreateAdminClientAsync($"owner_{Guid.NewGuid():N}@ulbra.br");
        var other = await factory.CreateAdminClientAsync($"other_{Guid.NewGuid():N}@ulbra.br");
        var (companyId, unitId, costCenterId, vendorId) = await SeedDimensionsAsync(owner);

        await CreateRequisitionAsync(owner, unitId, costCenterId, vendorId, 1_000m);

        // The owner sees their own Draft; another user's "mine" over the same company sees nothing.
        var mineOwner = (await Ok(await owner.GetAsync($"/api/requisitions/stage-counts?companyId={companyId}&mine=true")))
            .GetProperty("stages");
        Assert.Equal(1, mineOwner.GetProperty("Draft").GetProperty("count").GetInt32());

        var mineOther = (await Ok(await other.GetAsync($"/api/requisitions/stage-counts?companyId={companyId}&mine=true")))
            .GetProperty("stages");
        Assert.False(mineOther.TryGetProperty("Draft", out _));
    }

    [Fact]
    public async Task Requires_RequisitionsRead_Permission()
    {
        var reader = fixture.Api.CreateClientWith(Roles.Finance); // no permissions
        var res = await reader.GetAsync($"/api/requisitions/stage-counts?companyId=company_{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Anonymous_Returns_401()
    {
        var res = await fixture.Api.CreateClient().GetAsync("/api/requisitions/stage-counts");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    // --- RequisitionScope: consolidated guard + scoped branch (shared with ListRequisitions) ---

    [Fact]
    public async Task Omitting_CompanyId_Is_Forbidden_For_A_Non_Consolidated_Non_Scoped_Role()
    {
        // "User" is neither consolidated (Admin/Presidency/Controllership/Finance/Accounting) nor scoped
        // (Requester/Approver), so it must not be able to sweep every company via an omitted companyId.
        var reader = fixture.Api.CreateClientWith("User", Permissions.RequisitionsRead);
        var res = await reader.GetAsync("/api/requisitions/stage-counts");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("requisition.consolidated_forbidden", body.GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Consolidated_Role_Sees_All_Companies_And_Matches_The_List()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync($"boss_{Guid.NewGuid():N}@ulbra.br");
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionsAsync(admin);
        // Two Triage requisitions somewhere in the group.
        foreach (var _ in new[] { 0, 1 })
        {
            var id = await CreateRequisitionAsync(admin, unitId, costCenterId, vendorId, 1_000m);
            await Ok(await admin.PostAsJsonAsync($"/api/requisitions/{id}/submit", new { }));
        }

        // Finance is consolidated with no scope grant → the omitted companyId sweeps every company (no 403).
        var finance = factory.CreateClientWith(Roles.Finance, Permissions.RequisitionsRead);
        var stages = (await Ok(await finance.GetAsync("/api/requisitions/stage-counts"))).GetProperty("stages");
        var listTotal = (await Ok(await finance.GetAsync("/api/requisitions?status=Triage")))
            .GetProperty("total").GetInt32();

        var triage = stages.TryGetProperty("Triage", out var s) ? s.GetProperty("count").GetInt32() : 0;
        Assert.True(triage >= 2); // includes the two we created
        Assert.Equal(listTotal, triage); // parity: stage-counts == the list's total, both consolidated
    }

    [Fact]
    public async Task Scoped_Role_Counts_Only_Its_Scope_And_Matches_The_List()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync($"boss_{Guid.NewGuid():N}@ulbra.br");
        var companyId = await FinanceSeed.CreateCompanyAsync(admin);
        // Two dimensions in the SAME company: one in the requester's scope, one out of it.
        var (inUnit, inCc) = await SeedUnitAndCostCenterAsync(admin, companyId, "In Scope", "IN");
        var (outUnit, outCc) = await SeedUnitAndCostCenterAsync(admin, companyId, "Out Of Scope", "OUT");
        var vendorId = await FinanceSeed.CreateVendorAsync(admin, "Fornecedor Teste");

        // 3 Triage in-scope, 2 Triage out-of-scope.
        for (var i = 0; i < 3; i++) await SubmitTriageAsync(admin, inUnit, inCc, vendorId);
        for (var i = 0; i < 2; i++) await SubmitTriageAsync(admin, outUnit, outCc, vendorId);

        // Admin (unscoped) sees all 5 in this company; the scoped requester sees only its 3.
        var adminStages = (await Ok(await admin.GetAsync($"/api/requisitions/stage-counts?companyId={companyId}"))).GetProperty("stages");
        Assert.Equal(5, adminStages.GetProperty("Triage").GetProperty("count").GetInt32());

        var requester = factory.CreateClientWithUnitScope(
            Roles.Requester,
            [CompanyId.Parse(companyId, null).Value],
            [(CompanyId.Parse(companyId, null).Value, UnitId.Parse(inUnit, null).Value)],
            [(CompanyId.Parse(companyId, null).Value, CostCenterId.Parse(inCc, null).Value)],
            Permissions.RequisitionsRead);

        var scopedTriage = (await Ok(await requester.GetAsync($"/api/requisitions/stage-counts?companyId={companyId}")))
            .GetProperty("stages").GetProperty("Triage").GetProperty("count").GetInt32();
        var scopedListTotal = (await Ok(await requester.GetAsync($"/api/requisitions?companyId={companyId}&status=Triage")))
            .GetProperty("total").GetInt32();

        Assert.Equal(3, scopedTriage); // only the in-scope dimension
        Assert.Equal(scopedListTotal, scopedTriage); // parity with the list under the same scope
    }

    [Fact]
    public async Task Total_Sums_Every_Item_In_The_Status_Not_Just_One()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync($"boss_{Guid.NewGuid():N}@ulbra.br");
        var (companyId, unitId, costCenterId, vendorId) = await SeedDimensionsAsync(admin);
        await DriveToApprovalAsync(admin, unitId, costCenterId, vendorId, 1_500m);
        await DriveToApprovalAsync(admin, unitId, costCenterId, vendorId, 2_500m);

        var approval = (await Ok(await admin.GetAsync($"/api/requisitions/stage-counts?companyId={companyId}")))
            .GetProperty("stages").GetProperty("Approval");
        Assert.Equal(2, approval.GetProperty("count").GetInt32());
        Assert.Equal(4_000m, approval.GetProperty("total").GetDecimal()); // GROUP BY SUM, not a single row
    }

    private static async Task<(string UnitId, string CostCenterId)> SeedUnitAndCostCenterAsync(
        HttpClient admin, string companyId, string unitName, string acronym)
    {
        var unitId = await FinanceSeed.CreateUnitAsync(admin, companyId, unitName, acronym);
        var cc = await Ok(await admin.PostAsJsonAsync("/api/cost-centers", new
        {
            companyId,
            code = $"CC{Random.Shared.Next(100_000, 999_999)}",
            name = unitName,
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
        return (unitId, costCenterId);
    }

    private static async Task SubmitTriageAsync(HttpClient admin, string unitId, string costCenterId, string vendorId)
    {
        var id = await CreateRequisitionAsync(admin, unitId, costCenterId, vendorId, 1_000m);
        await Ok(await admin.PostAsJsonAsync($"/api/requisitions/{id}/submit", new { }));
    }

    private static async Task DriveToApprovalAsync(
        HttpClient admin, string unitId, string costCenterId, string vendorId, decimal value)
    {
        var id = await CreateRequisitionAsync(admin, unitId, costCenterId, vendorId, value);
        await Ok(await admin.PostAsJsonAsync($"/api/requisitions/{id}/submit", new { }));
        await Ok(await admin.PostAsJsonAsync($"/api/requisitions/{id}/approve-triage", new { }));
        await Ok(await admin.PostAsJsonAsync($"/api/requisitions/{id}/submit-financial-complement", new
        {
            issueDate = "2026-07-05",
            installments = new[] { new { dueDate = "2026-07-10", amount = value, paymentMethod = "Pix" } },
        }));
    }
}
