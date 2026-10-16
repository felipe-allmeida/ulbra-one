using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.IntegrationTests.Finance;
using Xunit;

namespace Ulbra.One.IntegrationTests.Budgeting;

/// <summary>
/// The duplicate-boleto check has to cross the module boundary: a requisition's payment plan holds linhas
/// digitáveis before Finance ever materializes a title, and Budgeting cannot read Finance's installments.
/// These cover both directions of that seam.
/// </summary>
[Collection("Postgres")]
public class RequisitionBoletoDuplicateTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> Ok(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static async Task<string> ErrorCodeOf(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement
            .GetProperty("error_code").GetString()!;

    private static async Task<(string UnitId, string CostCenterId, string VendorId, string CompanyId)> SeedAsync(
        HttpClient client)
    {
        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var unitId = await FinanceSeed.CreateUnitAsync(client, companyId, "Campus Teste", "CT");
        var cc = await Ok(await client.PostAsJsonAsync("/api/cost-centers", new
        {
            companyId,
            code = $"CC{Random.Shared.Next(100_000, 999_999)}",
            name = "Marketing",
            autoApprovalLimit = 0m,
            unitIds = new[] { unitId },
        }));
        var costCenterId = cc.GetProperty("id").GetString()!;
        await Ok(await client.PostAsJsonAsync($"/api/cost-centers/{costCenterId}/unit-budgets", new
        {
            unitId,
            startCompetency = "2026-07",
            endCompetency = "2026-12",
            monthlyLimit = 1_000_000m,
        }));
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Teste");
        return (unitId, costCenterId, vendorId, companyId);
    }

    /// <summary>A requisition parked at FinancialComplement, ready to receive a payment plan.</summary>
    private static async Task<string> RequisitionAwaitingPlanAsync(
        HttpClient client, string unitId, string costCenterId, string vendorId)
    {
        var created = await Ok(await client.PostAsJsonAsync("/api/requisitions", new
        {
            unitId,
            costCenterId,
            vendorId,
            type = "Purchase",
            description = "Comprar cadeiras",
            estimateValue = 300m,
            estimateType = "Full",
            startCompetency = "2026-07",
            firstDueDate = "2026-07-10",
        }));
        var id = created.GetProperty("id").GetString()!;
        await Ok(await client.PostAsJsonAsync($"/api/requisitions/{id}/submit", new { }));
        await Ok(await client.PostAsJsonAsync($"/api/requisitions/{id}/approve-triage", new { }));
        return id;
    }

    private static Task<HttpResponseMessage> SubmitPlanAsync(HttpClient client, string requisitionId, string line) =>
        client.PostAsJsonAsync($"/api/requisitions/{requisitionId}/submit-financial-complement", new
        {
            issueDate = "2026-07-05",
            installments = new[]
            {
                new { dueDate = "2026-07-10", amount = 300m, paymentMethod = "Boleto", boletoDigitableLine = line },
            },
        });

    private static Task<HttpResponseMessage> CreatePayableAsync(
        HttpClient client, string companyId, string costCenterId, string vendorId, string line) =>
        client.PostAsJsonAsync("/api/payables", new
        {
            companyId,
            vendorId,
            costCenterId,
            description = "Compra com boleto",
            installments = new[] { new { dueDate = "2026-07-10", amount = 300m, boletoDigitableLine = line } },
        });

    [Fact]
    public async Task A_requisition_plan_cannot_reuse_a_line_already_on_a_finance_title()
    {
        var client = await fixture.Api.CreateAdminClientAsync($"reqdup_{Guid.NewGuid():N}@ulbra.br");
        var (unitId, costCenterId, vendorId, companyId) = await SeedAsync(client);
        var line = FinanceSeed.NewBoletoLine();
        await Ok(await CreatePayableAsync(client, companyId, costCenterId, vendorId, line));
        var requisitionId = await RequisitionAwaitingPlanAsync(client, unitId, costCenterId, vendorId);

        var res = await SubmitPlanAsync(client, requisitionId, line);

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal(ErrorCodes.Requisition.BoletoDigitableLineDuplicate, await ErrorCodeOf(res));
    }

    [Fact]
    public async Task A_finance_title_cannot_reuse_a_line_already_on_a_requisition_plan()
    {
        var client = await fixture.Api.CreateAdminClientAsync($"reqdup_{Guid.NewGuid():N}@ulbra.br");
        var (unitId, costCenterId, vendorId, companyId) = await SeedAsync(client);
        var line = FinanceSeed.NewBoletoLine();
        var requisitionId = await RequisitionAwaitingPlanAsync(client, unitId, costCenterId, vendorId);
        await Ok(await SubmitPlanAsync(client, requisitionId, line));

        var res = await CreatePayableAsync(client, companyId, costCenterId, vendorId, line);

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal(ErrorCodes.Payable.BoletoDigitableLineDuplicate, await ErrorCodeOf(res));
    }

    [Fact]
    public async Task A_requisition_plan_with_a_fresh_line_goes_through()
    {
        var client = await fixture.Api.CreateAdminClientAsync($"reqdup_{Guid.NewGuid():N}@ulbra.br");
        var (unitId, costCenterId, vendorId, _) = await SeedAsync(client);
        var requisitionId = await RequisitionAwaitingPlanAsync(client, unitId, costCenterId, vendorId);

        var res = await SubmitPlanAsync(client, requisitionId, FinanceSeed.NewBoletoLine());

        Assert.True(res.IsSuccessStatusCode,
            $"HTTP {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
    }
}
