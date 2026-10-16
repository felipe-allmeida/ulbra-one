using System.Net.Http.Json;
using System.Text.Json;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Identity.Domain;
using Ulbra.One.IntegrationTests.Finance;
using Xunit;

namespace Ulbra.One.IntegrationTests.Budgeting;

/// <summary>AEL-883 recorte por escopo das leituras de orçamento: GET /requisitions/budget-summary e
/// /budget-projection só mostram a um papel escopado (Requester/Approver) as unidades × naturezas concedidas;
/// papéis não-escopados (Admin) seguem vendo a consolidação. Requester lê o escopo do JWT; o Aprovador final
/// lê ao vivo do diretório. Fora do escopo → vazio (fail-closed).</summary>
[Collection("Postgres")]
public class BudgetSummaryScopeTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> Ok(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    /// <summary>Creates a unit + cost center + a 1.000.000 budget cell over 2026-07..2026-12 in one call.</summary>
    private static async Task<(string UnitId, string CostCenterId)> SeedUnitCcBudgetAsync(
        HttpClient admin, string companyId, string name, string acronym)
    {
        var unitId = await FinanceSeed.CreateUnitAsync(admin, companyId, name, acronym);
        var cc = await Ok(await admin.PostAsJsonAsync("/api/cost-centers", new
        {
            companyId,
            code = $"CC{Random.Shared.Next(100_000, 999_999)}",
            name,
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

    private static List<string> CostCenterIdsOf(JsonElement summary) =>
        summary.EnumerateArray().Select(l => l.GetProperty("costCenterId").GetString()!).ToList();

    private static JsonElement MonthOf(JsonElement projection, string competency) =>
        projection.GetProperty("months").EnumerateArray().Single(m => m.GetProperty("competency").GetString() == competency);

    private static HttpClient ScopedRequester(ApiFactory factory, string companyId, string unitId, string costCenterId) =>
        factory.CreateClientWithUnitScope(
            Roles.Requester,
            [CompanyId.Parse(companyId, null).Value],
            [(CompanyId.Parse(companyId, null).Value, UnitId.Parse(unitId, null).Value)],
            [(CompanyId.Parse(companyId, null).Value, CostCenterId.Parse(costCenterId, null).Value)],
            Permissions.RequisitionsRead);

    [Fact]
    public async Task Summary_scoped_requester_sees_only_its_natureza_summed_over_its_unit()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync($"boss_{Guid.NewGuid():N}@ulbra.br");
        var companyId = await FinanceSeed.CreateCompanyAsync(admin);
        var (inUnit, inCc) = await SeedUnitCcBudgetAsync(admin, companyId, "In Scope", "IN");
        var (_, outCc) = await SeedUnitCcBudgetAsync(admin, companyId, "Out Of Scope", "OUT");

        // Admin (unscoped) sees both naturezas.
        var adminCcs = CostCenterIdsOf(await Ok(await admin.GetAsync(
            $"/api/requisitions/budget-summary?companyId={companyId}&competency=2026-07")));
        Assert.Contains(inCc, adminCcs);
        Assert.Contains(outCc, adminCcs);

        // Requester scoped to (inUnit, inCc) sees only its natureza, orçado summed over its unit alone.
        var requester = ScopedRequester(factory, companyId, inUnit, inCc);
        var scoped = await Ok(await requester.GetAsync(
            $"/api/requisitions/budget-summary?companyId={companyId}&competency=2026-07"));
        var line = Assert.Single(scoped.EnumerateArray().ToList());
        Assert.Equal(inCc, line.GetProperty("costCenterId").GetString());
        Assert.Equal(1_000_000m, line.GetProperty("orcado").GetDecimal());
    }

    [Fact]
    public async Task Summary_requested_unit_outside_scope_is_empty_fail_closed()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync($"boss_{Guid.NewGuid():N}@ulbra.br");
        var companyId = await FinanceSeed.CreateCompanyAsync(admin);
        var (inUnit, inCc) = await SeedUnitCcBudgetAsync(admin, companyId, "In Scope", "IN");
        var (outUnit, _) = await SeedUnitCcBudgetAsync(admin, companyId, "Out Of Scope", "OUT");

        // Picking a unit the requester is NOT scoped to yields nothing (requested ∩ scope = ∅).
        var requester = ScopedRequester(factory, companyId, inUnit, inCc);
        var res = await Ok(await requester.GetAsync(
            $"/api/requisitions/budget-summary?companyId={companyId}&competency=2026-07&unitId={outUnit}"));
        Assert.Empty(res.EnumerateArray());
    }

    [Fact]
    public async Task Summary_scoped_approver_reads_live_directory_scope()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync($"boss_{Guid.NewGuid():N}@ulbra.br");
        var companyId = await FinanceSeed.CreateCompanyAsync(admin);
        var (inUnit, inCc) = await SeedUnitCcBudgetAsync(admin, companyId, "In Scope", "IN");
        await SeedUnitCcBudgetAsync(admin, companyId, "Out Of Scope", "OUT");

        // The final Approver (approve, no triage/write) reads scope LIVE from the directory, not the JWT.
        var (approver, approverId) = await factory.CreatePersistedUserClientAsync(
            Roles.Approver, Permissions.RequisitionsRead, Permissions.RequisitionsApprove);
        await factory.SeedUserScopeAsync(approverId,
            CompanyId.Parse(companyId, null).Value, UnitId.Parse(inUnit, null).Value, CostCenterId.Parse(inCc, null).Value);

        var scoped = await Ok(await approver.GetAsync(
            $"/api/requisitions/budget-summary?companyId={companyId}&competency=2026-07"));
        var line = Assert.Single(scoped.EnumerateArray().ToList());
        Assert.Equal(inCc, line.GetProperty("costCenterId").GetString());
    }

    [Fact]
    public async Task Projection_scoped_requester_ceiling_covers_only_its_units_and_naturezas()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync($"boss_{Guid.NewGuid():N}@ulbra.br");
        var companyId = await FinanceSeed.CreateCompanyAsync(admin);
        var (inUnit, inCc) = await SeedUnitCcBudgetAsync(admin, companyId, "In Scope", "IN");
        await SeedUnitCcBudgetAsync(admin, companyId, "Out Of Scope", "OUT");

        // Admin: ceiling for 2026-07 sums both naturezas (1.000.000 + 1.000.000).
        var adminProj = await Ok(await admin.GetAsync(
            $"/api/requisitions/budget-projection?companyId={companyId}&from=2026-07&to=2026-07"));
        Assert.Equal(2_000_000m, MonthOf(adminProj, "2026-07").GetProperty("orcado").GetDecimal());

        // Requester scoped to (inUnit, inCc): ceiling covers only its natureza/unit.
        var requester = ScopedRequester(factory, companyId, inUnit, inCc);
        var scopedProj = await Ok(await requester.GetAsync(
            $"/api/requisitions/budget-projection?companyId={companyId}&from=2026-07&to=2026-07"));
        Assert.Equal(1_000_000m, MonthOf(scopedProj, "2026-07").GetProperty("orcado").GetDecimal());
    }
}
