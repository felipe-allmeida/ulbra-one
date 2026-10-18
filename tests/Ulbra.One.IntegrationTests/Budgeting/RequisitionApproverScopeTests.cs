using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Identity.Domain;
using Ulbra.One.Identity.Domain.Entities;
using Ulbra.One.IntegrationTests.Finance;
using Xunit;

namespace Ulbra.One.IntegrationTests.Budgeting;

/// <summary>Approver scope enforcement (AEL-883): the Approver approves and sees only requisitions within its
/// Empresa × Unidade × Centro de Custo scope, read LIVE from the DB (revocation is immediate), Admin bypasses.
/// The negative/inbox/revocation cases that the JWT-path unit tests and the happy-path decision-log test do
/// not cover.</summary>
[Collection("Postgres")]
public class RequisitionApproverScopeTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> Ok(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static async Task<(string CompanyId, string UnitId, string CostCenterId, string VendorId)> SeedApprovableDimensionAsync(HttpClient admin)
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

    /// <summary>Drives a fresh requisition in the given dimension all the way to Approval (as admin).</summary>
    private static async Task<string> DriveToApprovalAsync(HttpClient admin, string unitId, string costCenterId, string vendorId)
    {
        var created = await Ok(await admin.PostAsJsonAsync("/api/requisitions", new
        {
            unitId, costCenterId, vendorId, type = "Purchase", description = "Comprar cadeiras",
            observation = (string?)null, estimateValue = 1_000m, estimateType = "Full",
            startCompetency = "2026-07", endCompetency = (string?)null, firstDueDate = "2026-07-10",
            paymentFrequency = (string?)null, paymentInstallmentsCount = (int?)null,
        }));
        var id = created.GetProperty("id").GetString()!;
        await Ok(await admin.PostAsJsonAsync($"/api/requisitions/{id}/submit", new { }));
        await Ok(await admin.PostAsJsonAsync($"/api/requisitions/{id}/approve-triage", new { }));
        await Ok(await admin.PostAsJsonAsync($"/api/requisitions/{id}/submit-financial-complement", new
        {
            issueDate = "2026-07-05",
            installments = new[] { new { dueDate = "2026-07-10", amount = 1_000m, paymentMethod = "Pix" } },
        }));
        return id;
    }

    [Fact]
    public async Task Approve_Within_Scope_Returns_200()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync($"boss_{Guid.NewGuid():N}@ulbra.br");
        var (companyId, unitId, costCenterId, vendorId) = await SeedApprovableDimensionAsync(admin);
        var id = await DriveToApprovalAsync(admin, unitId, costCenterId, vendorId);

        var (approver, approverId) = await factory.CreatePersistedUserClientAsync(Roles.Approver,
            Permissions.RequisitionsRead, Permissions.RequisitionsApprove);
        await factory.SeedUserScopeAsync(approverId,
            CompanyId.Parse(companyId, null).Value, UnitId.Parse(unitId, null).Value, CostCenterId.Parse(costCenterId, null).Value);

        var res = await approver.PostAsJsonAsync($"/api/requisitions/{id}/approve", new { });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Approve_Outside_Scope_Returns_403()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync($"boss_{Guid.NewGuid():N}@ulbra.br");
        var (companyId, unitId, costCenterId, vendorId) = await SeedApprovableDimensionAsync(admin);
        var id = await DriveToApprovalAsync(admin, unitId, costCenterId, vendorId);

        var (approver, approverId) = await factory.CreatePersistedUserClientAsync(Roles.Approver,
            Permissions.RequisitionsRead, Permissions.RequisitionsApprove);
        // Company + unit granted, but a DIFFERENT cost center → the live intersection fails.
        await factory.SeedUserScopeAsync(approverId,
            CompanyId.Parse(companyId, null).Value, UnitId.Parse(unitId, null).Value, Guid.NewGuid());

        var res = await approver.PostAsJsonAsync($"/api/requisitions/{id}/approve", new { });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Contains("requisition.scope_forbidden", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Approve_Without_Any_Scope_Returns_403()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync($"boss_{Guid.NewGuid():N}@ulbra.br");
        var (_, unitId, costCenterId, vendorId) = await SeedApprovableDimensionAsync(admin);
        var id = await DriveToApprovalAsync(admin, unitId, costCenterId, vendorId);

        // Persisted approver with NO scope grants at all → fail-closed (empty scope covers nothing).
        var (approver, _) = await factory.CreatePersistedUserClientAsync(Roles.Approver,
            Permissions.RequisitionsRead, Permissions.RequisitionsApprove);

        var res = await approver.PostAsJsonAsync($"/api/requisitions/{id}/approve", new { });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Approve_Presidency_Bypasses_Scope_Returns_200()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync($"boss_{Guid.NewGuid():N}@ulbra.br");
        var (_, unitId, costCenterId, vendorId) = await SeedApprovableDimensionAsync(admin);
        var id = await DriveToApprovalAsync(admin, unitId, costCenterId, vendorId);

        // Presidency approves like Admin — bypasses the live scope check, no grant needed (AEL-883).
        var pres = factory.CreateClientWith(Roles.Presidency, Permissions.RequisitionsRead, Permissions.RequisitionsApprove);
        var res = await pres.PostAsJsonAsync($"/api/requisitions/{id}/approve", new { });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Inbox_Lists_Only_In_Scope_Requisitions()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync($"boss_{Guid.NewGuid():N}@ulbra.br");
        var (companyIn, unitIn, ccIn, vendorIn) = await SeedApprovableDimensionAsync(admin);
        var (_, unitOut, ccOut, vendorOut) = await SeedApprovableDimensionAsync(admin);
        var inScopeId = await DriveToApprovalAsync(admin, unitIn, ccIn, vendorIn);
        var outScopeId = await DriveToApprovalAsync(admin, unitOut, ccOut, vendorOut);

        var (approver, approverId) = await factory.CreatePersistedUserClientAsync(Roles.Approver,
            Permissions.RequisitionsRead, Permissions.RequisitionsApprove);
        await factory.SeedUserScopeAsync(approverId,
            CompanyId.Parse(companyIn, null).Value, UnitId.Parse(unitIn, null).Value, CostCenterId.Parse(ccIn, null).Value);

        // The Approver's inbox is the whole list (no filters): the isFinalApprover branch applies the live
        // intersection, so only the in-scope requisition is returned.
        var page = await Ok(await approver.GetAsync("/api/requisitions"));
        var ids = page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetString()).ToList();
        Assert.Contains(inScopeId, ids);
        Assert.DoesNotContain(outScopeId, ids);
    }

    [Fact]
    public async Task Get_Detail_Approver_Outside_Scope_Returns_404_Within_Returns_200()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync($"boss_{Guid.NewGuid():N}@ulbra.br");
        var (companyId, unitId, costCenterId, vendorId) = await SeedApprovableDimensionAsync(admin);
        var id = await DriveToApprovalAsync(admin, unitId, costCenterId, vendorId);

        var (outApprover, outId) = await factory.CreatePersistedUserClientAsync(Roles.Approver,
            Permissions.RequisitionsRead, Permissions.RequisitionsApprove);
        // Company + unit granted, but a DIFFERENT cost center → the live intersection fails (404-hide).
        await factory.SeedUserScopeAsync(outId,
            CompanyId.Parse(companyId, null).Value, UnitId.Parse(unitId, null).Value, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await outApprover.GetAsync($"/api/requisitions/{id}")).StatusCode);

        var (inApprover, inId) = await factory.CreatePersistedUserClientAsync(Roles.Approver,
            Permissions.RequisitionsRead, Permissions.RequisitionsApprove);
        await factory.SeedUserScopeAsync(inId,
            CompanyId.Parse(companyId, null).Value, UnitId.Parse(unitId, null).Value, CostCenterId.Parse(costCenterId, null).Value);
        Assert.Equal(HttpStatusCode.OK, (await inApprover.GetAsync($"/api/requisitions/{id}")).StatusCode);
    }

    [Fact]
    public async Task Revoking_Scope_Blocks_The_Next_Approve_Immediately()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync($"boss_{Guid.NewGuid():N}@ulbra.br");
        var (companyId, unitId, costCenterId, vendorId) = await SeedApprovableDimensionAsync(admin);
        var first = await DriveToApprovalAsync(admin, unitId, costCenterId, vendorId);
        var second = await DriveToApprovalAsync(admin, unitId, costCenterId, vendorId);

        var (approver, approverId) = await factory.CreatePersistedUserClientAsync(Roles.Approver,
            Permissions.RequisitionsRead, Permissions.RequisitionsApprove);
        await factory.SeedUserScopeAsync(approverId,
            CompanyId.Parse(companyId, null).Value, UnitId.Parse(unitId, null).Value, CostCenterId.Parse(costCenterId, null).Value);

        // In scope → approves.
        Assert.Equal(HttpStatusCode.OK, (await approver.PostAsJsonAsync($"/api/requisitions/{first}/approve", new { })).StatusCode);

        // Revoke the scope live (admin clears all grants). The Approver's token is UNCHANGED — the approve path
        // reads scope live, so the next approve is blocked immediately (no re-login needed).
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/users/{new UserId(approverId)}/scope",
            new { companyIds = Array.Empty<string>() })).StatusCode);

        var res = await approver.PostAsJsonAsync($"/api/requisitions/{second}/approve", new { });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Contains("requisition.scope_forbidden", await res.Content.ReadAsStringAsync());
    }
}
