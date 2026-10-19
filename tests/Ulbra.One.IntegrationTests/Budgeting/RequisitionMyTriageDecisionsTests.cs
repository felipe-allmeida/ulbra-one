using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Identity.Domain;
using Ulbra.One.IntegrationTests.Finance;
using Xunit;

namespace Ulbra.One.IntegrationTests.Budgeting;

/// <summary>GET /requisitions/my-triage-decisions: the controller's personal triage log — one row per
/// ApproveTriage/ReturnForComplement/Cancel of the CALLING user, newest first, vendor name resolved
/// server-side. Triage counterpart of <see cref="RequisitionMyDecisionsTests"/>.</summary>
[Collection("Postgres")]
public class RequisitionMyTriageDecisionsTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> Ok(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    // Drives a fresh requisition to the Triage state (created + submitted), where the controller acts.
    private static async Task<(string CompanyId, string Id)> DriveToTriageAsync(HttpClient admin)
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
        return (companyId, id);
    }

    private static HttpClient Controller(ApiFactory factory) =>
        factory.CreateClientWith(Roles.Controllership,
            Permissions.RequisitionsRead, Permissions.RequisitionsTriage);

    [Fact]
    public async Task Lists_Only_Callers_Triage_Decisions_Newest_First_With_Vendor_Name()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync($"boss_{Guid.NewGuid():N}@ulbra.br");
        var controller = Controller(factory);
        var otherController = Controller(factory);

        var (companyId, forwardedId) = await DriveToTriageAsync(admin);
        var (_, returnedId) = await DriveToTriageAsync(admin);
        var (_, cancelledId) = await DriveToTriageAsync(admin);
        var (_, foreignId) = await DriveToTriageAsync(admin);

        await Ok(await controller.PostAsJsonAsync($"/api/requisitions/{forwardedId}/approve-triage", new { }));
        await Ok(await controller.PostAsJsonAsync($"/api/requisitions/{returnedId}/return-for-complement", new { reason = "revisar fornecedor" }));
        await Ok(await controller.PostAsJsonAsync($"/api/requisitions/{cancelledId}/cancel", new { reason = "duplicada" }));
        // A triage decision by ANOTHER controller must not leak into the caller's log.
        await Ok(await otherController.PostAsJsonAsync($"/api/requisitions/{foreignId}/approve-triage", new { }));

        var page = await Ok(await controller.GetAsync("/api/requisitions/my-triage-decisions"));
        var items = page.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, items.Count);
        Assert.Equal(new[] { "Cancel", "ReturnForComplement", "ApproveTriage" },
            items.Select(i => i.GetProperty("decisionAction").GetString()).ToArray());
        Assert.Equal("duplicada", items[0].GetProperty("decisionReason").GetString());
        Assert.All(items, i => Assert.Equal("Fornecedor Teste", i.GetProperty("vendorName").GetString()));
        Assert.All(items, i => Assert.False(string.IsNullOrEmpty(i.GetProperty("decidedAt").GetString())));

        // companyId filter: the forwarded decision belongs to `companyId`; an unrelated company yields nothing.
        var filtered = await Ok(await controller.GetAsync($"/api/requisitions/my-triage-decisions?companyId={companyId}"));
        Assert.Single(filtered.GetProperty("items").EnumerateArray());
        var none = await Ok(await controller.GetAsync($"/api/requisitions/my-triage-decisions?companyId=company_{Guid.NewGuid():N}"));
        Assert.Empty(none.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Requires_Triage_Permission()
    {
        var factory = fixture.Api;
        var reader = factory.CreateClientWith(Roles.Finance, Permissions.RequisitionsRead);
        var res = await reader.GetAsync("/api/requisitions/my-triage-decisions");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Anonymous_Returns_401()
    {
        var res = await fixture.Api.CreateClient().GetAsync("/api/requisitions/my-triage-decisions");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
