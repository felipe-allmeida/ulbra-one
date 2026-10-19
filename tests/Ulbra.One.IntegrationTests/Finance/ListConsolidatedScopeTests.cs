using System.Net;
using System.Text.Json;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Identity.Domain;

namespace Ulbra.One.IntegrationTests.Finance;

/// <summary>
/// AEL-906: the operational Contas a Pagar / Contas a Receber lists accept an omitted companyId
/// (consolidated "Todas as empresas") but only for a consolidated role — mirrors the sibling stage /
/// aging / cash-flow endpoints. Guards the gap that let the 400/consolidated bug ship.
/// </summary>
[Collection("Postgres")]
public class ListConsolidatedScopeTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private static HashSet<decimal> Amounts(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("amount").GetDecimal()).ToHashSet();

    // ---- Payables ----------------------------------------------------------------------------

    [Fact]
    public async Task ListPayables_consolidated_returns_all_companies_and_still_filters_by_company()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        // A unique far-future due date isolates this test's rows on the shared DB (the consolidated read has no companyId).
        var due = FinanceSeed.Today().AddYears(20).AddDays(Random.Shared.Next(0, 300));

        var companyA = await FinanceSeed.CreateCompanyAsync(client);
        var ccA = await FinanceSeed.CreateCostCenterAsync(client, companyA, "CC1", "Geral");
        var vendorA = await FinanceSeed.CreateVendorAsync(client, "Fornecedor A");
        await FinanceSeed.CreatePayableAsync(factory, client, companyA, vendorA, ccA, due, 11_111m);

        var companyB = await FinanceSeed.CreateCompanyAsync(client);
        var ccB = await FinanceSeed.CreateCostCenterAsync(client, companyB, "CC1", "Geral");
        var vendorB = await FinanceSeed.CreateVendorAsync(client, "Fornecedor B");
        await FinanceSeed.CreatePayableAsync(factory, client, companyB, vendorB, ccB, due, 22_222m);

        var window = $"dueFrom={due:yyyy-MM-dd}&dueTo={due:yyyy-MM-dd}&pageSize=100";

        // Consolidated (no companyId): both companies' installments.
        var all = Amounts(await ReadJson(await client.GetAsync($"/api/payables?{window}")));
        Assert.Contains(11_111m, all);
        Assert.Contains(22_222m, all);

        // Single-company (unchanged): only that company's installment.
        var perA = Amounts(await ReadJson(await client.GetAsync($"/api/payables?companyId={companyA}&{window}")));
        Assert.Contains(11_111m, perA);
        Assert.DoesNotContain(22_222m, perA);
    }

    [Fact]
    public async Task ListPayables_consolidated_is_forbidden_for_a_non_consolidated_role()
    {
        var client = fixture.Api.CreateClientWith(Roles.Requester, Permissions.PayablesRead);
        var res = await client.GetAsync("/api/payables"); // companyId omitted
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("payable.consolidated_forbidden", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    // ---- Receivables -------------------------------------------------------------------------

    [Fact]
    public async Task ListReceivables_consolidated_returns_all_companies_and_still_filters_by_company()
    {
        var client = await fixture.Api.CreateAdminClientAsync();
        var due = FinanceSeed.Today().AddYears(20).AddDays(Random.Shared.Next(300, 600));

        var companyA = await FinanceSeed.CreateCompanyAsync(client);
        var ccA = await FinanceSeed.CreateCostCenterAsync(client, companyA, "CC1", "Geral");
        var payerA = await FinanceSeed.CreateCustomerAsync(client, companyA, "Cliente A");
        await FinanceSeed.CreateReceivableAsync(client, companyA, payerA, ccA, due, 33_333m);

        var companyB = await FinanceSeed.CreateCompanyAsync(client);
        var ccB = await FinanceSeed.CreateCostCenterAsync(client, companyB, "CC1", "Geral");
        var payerB = await FinanceSeed.CreateCustomerAsync(client, companyB, "Cliente B");
        await FinanceSeed.CreateReceivableAsync(client, companyB, payerB, ccB, due, 44_444m);

        var window = $"dueFrom={due:yyyy-MM-dd}&dueTo={due:yyyy-MM-dd}&pageSize=100";

        var all = Amounts(await ReadJson(await client.GetAsync($"/api/receivables?{window}")));
        Assert.Contains(33_333m, all);
        Assert.Contains(44_444m, all);

        var perA = Amounts(await ReadJson(await client.GetAsync($"/api/receivables?companyId={companyA}&{window}")));
        Assert.Contains(33_333m, perA);
        Assert.DoesNotContain(44_444m, perA);
    }

    [Fact]
    public async Task ListReceivables_consolidated_is_forbidden_for_a_non_consolidated_role()
    {
        var client = fixture.Api.CreateClientWith(Roles.Requester, Permissions.ReceivablesRead);
        var res = await client.GetAsync("/api/receivables"); // companyId omitted
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("receivable.consolidated_forbidden", (await ReadJson(res)).GetProperty("error_code").GetString());
    }
}
