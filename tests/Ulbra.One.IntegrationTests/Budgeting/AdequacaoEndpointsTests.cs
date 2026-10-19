using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Identity.Domain;
using Ulbra.One.IntegrationTests.Finance;
using Xunit;

namespace Ulbra.One.IntegrationTests.Budgeting;

/// <summary>Budget-adjustment (adequação) endpoints — AEL-791. Each test seeds its own company/unit/cost
/// center, so fixed competencies never collide across tests on the shared database. The approve path
/// mutates the real Core budget cell, verified via GET /api/cost-centers/{id}/unit-budgets.</summary>
[Collection("Postgres")]
public class BudgetAdjustmentEndpointsTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private static async Task<JsonElement> Ok(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static string UniqueEmail() => $"boss_{Guid.NewGuid():N}@ulbra.br";
    private static string Status(JsonElement a) => a.GetProperty("status").GetString()!;

    private static async Task<(HttpClient Client, string UserId)> AdminAsync(ApiFactory factory)
    {
        var client = await factory.CreateAdminClientAsync(UniqueEmail());
        var me = await Ok(await client.GetAsync("/api/auth/me"));
        return (client, me.GetProperty("userId").GetString()!);
    }

    private static async Task<(string CompanyId, string UnitId, string CostCenterId)> SeedDimensionAsync(HttpClient client)
    {
        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var unitId = await FinanceSeed.CreateUnitAsync(client, companyId, "Campus Teste", "CT");
        // The unit must be associated with the cost center (CostCenterUnit) — required by the budget/adequação flow.
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, $"CC{Random.Shared.Next(100_000, 999_999)}", "Marketing", unitId);
        return (companyId, unitId, costCenterId);
    }

    private static async Task SetAuthorityAsync(HttpClient client, string userId, decimal? approvalLimit, bool autonomy = false) =>
        await Ok(await client.PutAsJsonAsync($"/api/users/{userId}/approval-authority",
            new { approvalLimit, hasApprovalAutonomy = autonomy }));

    // A single-month cell (end == start) — o orçamento agora vive no Core (célula do CostCenter, por período).
    private static async Task CreateBudgetAsync(HttpClient client, string unitId, string costCenterId, string competency, decimal limit) =>
        await Ok(await client.PostAsJsonAsync($"/api/cost-centers/{costCenterId}/unit-budgets",
            new { unitId, startCompetency = competency, endCompetency = competency, monthlyLimit = limit, active = true }));

    private static async Task<decimal?> CellLimitAsync(HttpClient client, string unitId, string costCenterId, string competency)
    {
        var list = await Ok(await client.GetAsync($"/api/cost-centers/{costCenterId}/unit-budgets?unitId={unitId}&competencyFrom={competency}&competencyTo={competency}"));
        var items = list.EnumerateArray().ToList();
        return items.Count == 0 ? null : items[0].GetProperty("monthlyLimit").GetDecimal();
    }

    private static async Task<HttpResponseMessage> CreateBudgetAdjustmentAsync(
        HttpClient client, string unitId, string costCenterId, string type, string competency,
        string? sourceCompetency, decimal value, string? counterparts = "Entregas") =>
        await client.PostAsJsonAsync("/api/budget-adjustments", new
        {
            unitId,
            costCenterId,
            type,
            competency,
            sourceCompetency,
            value,
            justification = "Justificativa da adequação",
            counterparts,
        });

    private static Task<HttpResponseMessage> PostAction(HttpClient client, string id, string action) =>
        client.PostAsJsonAsync($"/api/budget-adjustments/{id}/{action}", new { });

    // ---- Suplementação ---------------------------------------------------------------------------

    [Fact]
    public async Task Supplementation_Approve_Raises_Existing_Ceiling()
    {
        var factory = fixture.Api;
        var (client, _) = await AdminAsync(factory);
        var (_, unitId, costCenterId) = await SeedDimensionAsync(client);
        await CreateBudgetAsync(client, unitId, costCenterId, "2040-01", 1_000m);

        var id = (await Ok(await CreateBudgetAdjustmentAsync(client, unitId, costCenterId, "Supplementation", "2040-01", null, 500m)))
            .GetProperty("id").GetString()!;
        await Ok(await PostAction(client, id, "send-to-approval"));
        Assert.Equal("Approved", Status(await Ok(await PostAction(client, id, "approve"))));

        Assert.Equal(1_500m, await CellLimitAsync(client, unitId, costCenterId, "2040-01"));
    }

    [Fact]
    public async Task Supplementation_Approve_Creates_Missing_Cell()
    {
        var factory = fixture.Api;
        var (client, _) = await AdminAsync(factory);
        var (_, unitId, costCenterId) = await SeedDimensionAsync(client);

        Assert.Null(await CellLimitAsync(client, unitId, costCenterId, "2041-05")); // no cell yet

        var id = (await Ok(await CreateBudgetAdjustmentAsync(client, unitId, costCenterId, "Supplementation", "2041-05", null, 800m)))
            .GetProperty("id").GetString()!;
        await Ok(await PostAction(client, id, "send-to-approval"));
        await Ok(await PostAction(client, id, "approve"));

        Assert.Equal(800m, await CellLimitAsync(client, unitId, costCenterId, "2041-05"));
    }

    // ---- Redução ---------------------------------------------------------------------------------

    [Fact]
    public async Task Reduction_Approve_Lowers_Ceiling()
    {
        var factory = fixture.Api;
        var (client, _) = await AdminAsync(factory);
        var (_, unitId, costCenterId) = await SeedDimensionAsync(client);
        await CreateBudgetAsync(client, unitId, costCenterId, "2042-03", 1_000m);

        var id = (await Ok(await CreateBudgetAdjustmentAsync(client, unitId, costCenterId, "Reduction", "2042-03", null, 300m, counterparts: null)))
            .GetProperty("id").GetString()!;
        await Ok(await PostAction(client, id, "send-to-approval"));
        await Ok(await PostAction(client, id, "approve"));

        Assert.Equal(700m, await CellLimitAsync(client, unitId, costCenterId, "2042-03"));
    }

    [Fact]
    public async Task Reduction_Below_Zero_Returns_409_And_Leaves_Ceiling()
    {
        var factory = fixture.Api;
        var (client, _) = await AdminAsync(factory);
        var (_, unitId, costCenterId) = await SeedDimensionAsync(client);
        await CreateBudgetAsync(client, unitId, costCenterId, "2043-07", 200m);

        var id = (await Ok(await CreateBudgetAdjustmentAsync(client, unitId, costCenterId, "Reduction", "2043-07", null, 500m, counterparts: null)))
            .GetProperty("id").GetString()!;
        await Ok(await PostAction(client, id, "send-to-approval"));

        var res = await PostAction(client, id, "approve");
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal(ErrorCodes.BudgetAdjustment.LimitWouldGoNegative, (await ReadJson(res)).GetProperty("error_code").GetString());
        Assert.Equal(200m, await CellLimitAsync(client, unitId, costCenterId, "2043-07")); // unchanged
    }

    [Fact]
    public async Task Reduction_Without_Cell_Returns_409_CellNotFound()
    {
        var factory = fixture.Api;
        var (client, _) = await AdminAsync(factory);
        var (_, unitId, costCenterId) = await SeedDimensionAsync(client);

        var id = (await Ok(await CreateBudgetAdjustmentAsync(client, unitId, costCenterId, "Reduction", "2044-02", null, 100m, counterparts: null)))
            .GetProperty("id").GetString()!;
        await Ok(await PostAction(client, id, "send-to-approval"));

        var res = await PostAction(client, id, "approve");
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal(ErrorCodes.BudgetAdjustment.CellNotFound, (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    // ---- Transferência ---------------------------------------------------------------------------

    [Fact]
    public async Task Transfer_Moves_Value_Between_Competencies()
    {
        var factory = fixture.Api;
        var (client, _) = await AdminAsync(factory);
        var (_, unitId, costCenterId) = await SeedDimensionAsync(client);
        await CreateBudgetAsync(client, unitId, costCenterId, "2045-01", 1_000m); // source
        await CreateBudgetAsync(client, unitId, costCenterId, "2045-02", 200m);   // destination

        var id = (await Ok(await CreateBudgetAdjustmentAsync(client, unitId, costCenterId, "Transfer", "2045-02", "2045-01", 300m)))
            .GetProperty("id").GetString()!;
        await Ok(await PostAction(client, id, "send-to-approval"));
        await Ok(await PostAction(client, id, "approve"));

        Assert.Equal(700m, await CellLimitAsync(client, unitId, costCenterId, "2045-01")); // source − 300
        Assert.Equal(500m, await CellLimitAsync(client, unitId, costCenterId, "2045-02")); // destination + 300
    }

    [Fact]
    public async Task Transfer_Without_Source_Returns_400()
    {
        var factory = fixture.Api;
        var (client, _) = await AdminAsync(factory);
        var (_, unitId, costCenterId) = await SeedDimensionAsync(client);

        var res = await CreateBudgetAdjustmentAsync(client, unitId, costCenterId, "Transfer", "2046-02", null, 100m);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(ErrorCodes.BudgetAdjustment.SourceRequired, (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    // ---- Reversão --------------------------------------------------------------------------------

    [Fact]
    public async Task Reverse_Recomposes_The_Budget()
    {
        var factory = fixture.Api;
        var (client, _) = await AdminAsync(factory);
        var (_, unitId, costCenterId) = await SeedDimensionAsync(client);
        await CreateBudgetAsync(client, unitId, costCenterId, "2047-09", 1_000m);

        var id = (await Ok(await CreateBudgetAdjustmentAsync(client, unitId, costCenterId, "Supplementation", "2047-09", null, 500m)))
            .GetProperty("id").GetString()!;
        await Ok(await PostAction(client, id, "send-to-approval"));
        await Ok(await PostAction(client, id, "approve"));
        Assert.Equal(1_500m, await CellLimitAsync(client, unitId, costCenterId, "2047-09"));

        var reversed = await Ok(await client.PostAsJsonAsync($"/api/budget-adjustments/{id}/reverse", new { reason = "engano" }));
        Assert.Equal("Reversed", Status(reversed));
        Assert.Equal(1_000m, await CellLimitAsync(client, unitId, costCenterId, "2047-09")); // recomposed
    }

    // ---- Alçada ----------------------------------------------------------------------------------

    [Fact]
    public async Task Approve_Above_Limit_Blocked_409_Even_For_Admin()
    {
        var factory = fixture.Api;
        var (client, userId) = await AdminAsync(factory);
        await SetAuthorityAsync(client, userId, approvalLimit: 1_000m);
        var (_, unitId, costCenterId) = await SeedDimensionAsync(client);
        await CreateBudgetAsync(client, unitId, costCenterId, "2048-04", 10_000m);

        var id = (await Ok(await CreateBudgetAdjustmentAsync(client, unitId, costCenterId, "Supplementation", "2048-04", null, 5_000m)))
            .GetProperty("id").GetString()!;
        await Ok(await PostAction(client, id, "send-to-approval"));

        var res = await PostAction(client, id, "approve");
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal(ErrorCodes.BudgetAdjustment.ApprovalLimitExceeded, (await ReadJson(res)).GetProperty("error_code").GetString());
        Assert.Equal(10_000m, await CellLimitAsync(client, unitId, costCenterId, "2048-04")); // unchanged
    }

    // ---- Create / cancel + lifecycle -------------------------------------------------------------

    [Fact]
    public async Task Create_Starts_AwaitingTriage_And_Cancel_Works()
    {
        var factory = fixture.Api;
        var (client, _) = await AdminAsync(factory);
        var (companyId, unitId, costCenterId) = await SeedDimensionAsync(client);

        var created = await Ok(await CreateBudgetAdjustmentAsync(client, unitId, costCenterId, "Supplementation", "2049-01", null, 250m));
        Assert.Equal("AwaitingTriage", Status(created));
        Assert.Equal(companyId, created.GetProperty("companyId").GetString()); // derived from unit
        Assert.Equal("Supplementation", created.GetProperty("type").GetString());
        var id = created.GetProperty("id").GetString()!;

        var cancelled = await Ok(await client.PostAsJsonAsync($"/api/budget-adjustments/{id}/cancel", new { reason = "desistiu" }));
        Assert.Equal("Cancelled", Status(cancelled));
        Assert.Equal("desistiu", cancelled.GetProperty("transitions").EnumerateArray().Last().GetProperty("reason").GetString());
    }

    // ---- Permission gating -----------------------------------------------------------------------

    [Fact]
    public async Task Create_Without_Write_Permission_Returns_403()
    {
        var factory = fixture.Api;
        var client = factory.CreateClientWith(Roles.Finance, Permissions.BudgetAdjustmentsRead);
        var res = await client.PostAsJsonAsync("/api/budget-adjustments", new { });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Approve_Without_Approve_Permission_Returns_403()
    {
        var factory = fixture.Api;
        var client = factory.CreateClientWith(Roles.Finance, Permissions.BudgetAdjustmentsWrite);
        var res = await client.PostAsJsonAsync($"/api/budget-adjustments/budgetadj_{Guid.NewGuid():N}/approve", new { });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Anonymous_Returns_401()
    {
        var factory = fixture.Api;
        var client = factory.CreateClient();
        var res = await client.GetAsync("/api/budget-adjustments");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    // ---- Per-user approval grant (AEL-883): Controladoria holds the permission, but only granted users approve ----

    private static async Task<JsonElement> SetApproverFlagAsync(HttpClient admin, Guid userId, bool canApprove) =>
        await Ok(await admin.PutAsJsonAsync($"/api/users/{new UserId(userId)}/adjustment-approver", new { canApprove }));

    [Fact]
    public async Task Approve_Controladoria_Without_Grant_Returns_403()
    {
        var factory = fixture.Api;
        var (admin, _) = await AdminAsync(factory);
        var (_, unitId, costCenterId) = await SeedDimensionAsync(admin);
        var id = (await Ok(await CreateBudgetAdjustmentAsync(admin, unitId, costCenterId, "Supplementation", "2050-01", null, 500m))).GetProperty("id").GetString()!;
        await Ok(await PostAction(admin, id, "send-to-approval"));

        // A Controladoria user holding budget-adjustments:approve but WITHOUT the per-user grant is blocked.
        var (controladoria, _) = await factory.CreatePersistedUserClientAsync(Roles.Controllership,
            Permissions.BudgetAdjustmentsRead, Permissions.BudgetAdjustmentsApprove);
        var res = await PostAction(controladoria, id, "approve");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("budget_adjustment.approver_not_authorized", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Approve_Controladoria_With_Grant_Succeeds()
    {
        var factory = fixture.Api;
        var (admin, _) = await AdminAsync(factory);
        var (_, unitId, costCenterId) = await SeedDimensionAsync(admin);
        var id = (await Ok(await CreateBudgetAdjustmentAsync(admin, unitId, costCenterId, "Supplementation", "2050-02", null, 500m))).GetProperty("id").GetString()!;
        await Ok(await PostAction(admin, id, "send-to-approval"));

        var (controladoria, controladoriaId) = await factory.CreatePersistedUserClientAsync(Roles.Controllership,
            Permissions.BudgetAdjustmentsRead, Permissions.BudgetAdjustmentsApprove);
        await SetApproverFlagAsync(admin, controladoriaId, true);

        Assert.Equal("Approved", Status(await Ok(await PostAction(controladoria, id, "approve"))));
        Assert.Equal(500m, await CellLimitAsync(admin, unitId, costCenterId, "2050-02"));
    }

    [Fact]
    public async Task Reverse_Controladoria_Without_Grant_Returns_403()
    {
        var factory = fixture.Api;
        var (admin, _) = await AdminAsync(factory);
        var (_, unitId, costCenterId) = await SeedDimensionAsync(admin);
        var id = (await Ok(await CreateBudgetAdjustmentAsync(admin, unitId, costCenterId, "Supplementation", "2050-03", null, 500m))).GetProperty("id").GetString()!;
        await Ok(await PostAction(admin, id, "send-to-approval"));
        await Ok(await PostAction(admin, id, "approve")); // Admin bypasses the grant → Approved

        var (controladoria, _) = await factory.CreatePersistedUserClientAsync(Roles.Controllership,
            Permissions.BudgetAdjustmentsRead, Permissions.BudgetAdjustmentsApprove);
        var res = await controladoria.PostAsJsonAsync($"/api/budget-adjustments/{id}/reverse", new { reason = "x" });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("budget_adjustment.approver_not_authorized", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Revoking_Grant_Blocks_The_Next_Approve_Immediately()
    {
        var factory = fixture.Api;
        var (admin, _) = await AdminAsync(factory);
        var (_, unitId, costCenterId) = await SeedDimensionAsync(admin);
        var first = (await Ok(await CreateBudgetAdjustmentAsync(admin, unitId, costCenterId, "Supplementation", "2050-04", null, 300m))).GetProperty("id").GetString()!;
        var second = (await Ok(await CreateBudgetAdjustmentAsync(admin, unitId, costCenterId, "Supplementation", "2050-05", null, 300m))).GetProperty("id").GetString()!;
        await Ok(await PostAction(admin, first, "send-to-approval"));
        await Ok(await PostAction(admin, second, "send-to-approval"));

        var (controladoria, controladoriaId) = await factory.CreatePersistedUserClientAsync(Roles.Controllership,
            Permissions.BudgetAdjustmentsRead, Permissions.BudgetAdjustmentsApprove);
        await SetApproverFlagAsync(admin, controladoriaId, true);
        Assert.Equal("Approved", Status(await Ok(await PostAction(controladoria, first, "approve"))));

        // Revoke the grant live (same token) → the next approve is blocked immediately (read live).
        await SetApproverFlagAsync(admin, controladoriaId, false);
        var res = await PostAction(controladoria, second, "approve");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("budget_adjustment.approver_not_authorized", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task SetAdjustmentApprover_RoundTrips_And_Me_Reflects_It()
    {
        var factory = fixture.Api;
        var (admin, _) = await AdminAsync(factory);
        var (controladoria, controladoriaId) = await factory.CreatePersistedUserClientAsync(Roles.Controllership,
            Permissions.BudgetAdjustmentsRead, Permissions.BudgetAdjustmentsApprove);

        var me0 = await Ok(await controladoria.GetAsync("/api/auth/me"));
        Assert.False(me0.GetProperty("canApproveBudgetAdjustment").GetBoolean());

        var granted = await SetApproverFlagAsync(admin, controladoriaId, true);
        Assert.True(granted.GetProperty("canApproveBudgetAdjustment").GetBoolean());

        var me1 = await Ok(await controladoria.GetAsync("/api/auth/me"));
        Assert.True(me1.GetProperty("canApproveBudgetAdjustment").GetBoolean());
    }

    [Fact]
    public async Task Approve_Presidency_Without_Grant_Succeeds()
    {
        var factory = fixture.Api;
        var (admin, _) = await AdminAsync(factory);
        var (_, unitId, costCenterId) = await SeedDimensionAsync(admin);
        var id = (await Ok(await CreateBudgetAdjustmentAsync(admin, unitId, costCenterId, "Supplementation", "2051-01", null, 500m))).GetProperty("id").GetString()!;
        await Ok(await PostAction(admin, id, "send-to-approval"));

        // Presidency bypasses the per-user grant (like Admin) — no SetApproverFlag, no persisted profile needed.
        var pres = factory.CreateClientWith(Roles.Presidency, Permissions.BudgetAdjustmentsRead, Permissions.BudgetAdjustmentsApprove);
        Assert.Equal("Approved", Status(await Ok(await PostAction(pres, id, "approve"))));
    }

    [Fact]
    public async Task SetAdjustmentApprover_Without_Admin_Returns_403()
    {
        var factory = fixture.Api;
        var client = factory.CreateClientWith(Roles.Controllership, Permissions.BudgetAdjustmentsApprove);
        var res = await client.PutAsJsonAsync($"/api/users/{new UserId(Guid.NewGuid())}/adjustment-approver", new { canApprove = true });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
