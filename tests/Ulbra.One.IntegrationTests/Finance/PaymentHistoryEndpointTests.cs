using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Identity.Domain;

namespace Ulbra.One.IntegrationTests.Finance;

/// <summary>
/// Covers GET /api/payables/payments — the cross-payable settlement history (Histórico de baixas):
/// ordering, date/bank/method filters, search, reversed visibility, company scoping and pagination.
/// </summary>
[Collection("Postgres")]
public class PaymentHistoryEndpointTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private static string FormatCnpj(string digits) =>
        $"{digits[..2]}.{digits[2..5]}.{digits[5..8]}/{digits[8..12]}-{digits[12..]}";

    // ---------------------------------------------------------------------------
    // Seed: 2 Scheduled payables (different vendors), 2 bank accounts, 3 payments
    //   X: payable A, today-5, Pix,    bank1, 400 (partial)
    //   Y: payable A, today-1, Ted,    bank2, 300 (partial)
    //   Z: payable B, today-3, Boleto, bank2, 500 (full)
    // ---------------------------------------------------------------------------

    private sealed record Seed(
        string CompanyId,
        string VendorADigits, string VendorBDigits,
        string PayableAId, string PayableBId,
        string Bank1Id, string Bank2Id,
        string PaymentXId, string PaymentYId, string PaymentZId,
        DateOnly Today);

    private static async Task<Seed> SeedWithPaymentsAsync(ApiFactory factory, HttpClient client)
    {
        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, "CC-HIST", "Histórico");

        var digitsA = FinanceSeed.RandomCnpj();
        var digitsB = FinanceSeed.RandomCnpj();
        var vendorA = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Historia Alfa", digitsA);
        var vendorB = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Historia Beta", digitsB);

        var bank1 = await FinanceSeed.CreateBankAccountAsync(client, companyId, Random.Shared.Next(10_000_000, 99_999_999).ToString());
        var bank2 = await FinanceSeed.CreateBankAccountAsync(client, companyId, Random.Shared.Next(10_000_000, 99_999_999).ToString());

        // Brazil-local today (matches the API's business clock for payment-date validation).
        var today = FinanceSeed.Today();

        var (payableA, installmentA) = await FinanceSeed.CreatePayableWithInstallmentAsync(
            factory, client, companyId, vendorA, costCenterId, today.AddDays(30), 1_000m);
        var (payableB, installmentB) = await FinanceSeed.CreatePayableWithInstallmentAsync(
            factory, client, companyId, vendorB, costCenterId, today.AddDays(30), 500m);

        var paymentX = await PayAsync(client, payableA, installmentA, bank1, 400m, today.AddDays(-5), "Pix");
        var paymentY = await PayAsync(client, payableA, installmentA, bank2, 300m, today.AddDays(-1), "Ted");
        var paymentZ = await PayAsync(client, payableB, installmentB, bank2, 500m, today.AddDays(-3), "Boleto");

        return new Seed(companyId, digitsA, digitsB, payableA, payableB, bank1, bank2,
            paymentX, paymentY, paymentZ, today);
    }

    /// <summary>Registers a baixa and returns the created payment's id (matched by amount, unique per seed).</summary>
    private static async Task<string> PayAsync(
        HttpClient client, string payableId, string installmentId, string bankId, decimal amount, DateOnly date, string method)
    {
        var res = await client.PostAsJsonAsync($"/api/payables/{payableId}/installments/{installmentId}/payments", new
        {
            paymentDate = date.ToString("yyyy-MM-dd"),
            amount,
            bankAccountId = bankId,
            method,
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var payments = (await ReadJson(res)).GetProperty("installments")[0].GetProperty("payments");
        return payments.EnumerateArray()
            .Single(p => p.GetProperty("amount").GetDecimal() == amount)
            .GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> GetHistoryAsync(HttpClient client, string query)
    {
        var res = await client.GetAsync($"/api/payables/payments?{query}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return await ReadJson(res);
    }

    private static List<string> Ids(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("paymentId").GetString()!).ToList();

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task History_Orders_By_PaymentDate_Desc_And_Paginates_With_Total()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedWithPaymentsAsync(factory, client);

        var page = await GetHistoryAsync(client, $"companyId={seed.CompanyId}&pageSize=50");
        // Y (today-1) > Z (today-3) > X (today-5).
        Assert.Equal(new[] { seed.PaymentYId, seed.PaymentZId, seed.PaymentXId }, Ids(page));
        Assert.Equal(3, page.GetProperty("total").GetInt32());

        // Row shape sanity on the oldest payment (X).
        var rowX = page.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("paymentId").GetString() == seed.PaymentXId);
        Assert.Equal(seed.PayableAId, rowX.GetProperty("payableId").GetString());
        Assert.Equal(1, rowX.GetProperty("installmentSequence").GetInt32());
        Assert.Equal(1, rowX.GetProperty("installmentCount").GetInt32());
        Assert.Equal("Fornecedor Historia Alfa", rowX.GetProperty("counterpartyName").GetString());
        Assert.Equal(seed.VendorADigits, rowX.GetProperty("vendorDocument").GetString());
        Assert.Equal(400m, rowX.GetProperty("amount").GetDecimal());
        Assert.Equal("Pix", rowX.GetProperty("method").GetString());
        Assert.Equal(seed.Bank1Id, rowX.GetProperty("bankAccountId").GetString());
        Assert.False(rowX.GetProperty("isReversed").GetBoolean());
        Assert.False(rowX.GetProperty("hasProof").GetBoolean());

        // Pagination: 1 item per page, same total.
        var firstPage = await GetHistoryAsync(client, $"companyId={seed.CompanyId}&pageSize=1");
        Assert.Single(firstPage.GetProperty("items").EnumerateArray());
        Assert.Equal(3, firstPage.GetProperty("total").GetInt32());
        Assert.Equal(seed.PaymentYId, Ids(firstPage).Single());
    }

    [Fact]
    public async Task History_Filters_By_DateRange_BankAccount_And_Method()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedWithPaymentsAsync(factory, client);

        // Inclusive PaymentDate window catching only Z (today-3).
        var from = seed.Today.AddDays(-4).ToString("yyyy-MM-dd");
        var to = seed.Today.AddDays(-2).ToString("yyyy-MM-dd");
        var byDate = await GetHistoryAsync(client, $"companyId={seed.CompanyId}&from={from}&to={to}");
        Assert.Equal(new[] { seed.PaymentZId }, Ids(byDate));

        var byBank = await GetHistoryAsync(client, $"companyId={seed.CompanyId}&bankAccountId={seed.Bank1Id}");
        Assert.Equal(new[] { seed.PaymentXId }, Ids(byBank));

        var byMethod = await GetHistoryAsync(client, $"companyId={seed.CompanyId}&method=Ted");
        Assert.Equal(new[] { seed.PaymentYId }, Ids(byMethod));
    }

    [Fact]
    public async Task History_Search_By_Counterparty_And_By_Formatted_Cnpj()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedWithPaymentsAsync(factory, client);

        // Case-insensitive counterparty substring → both payments of payable A.
        var byName = await GetHistoryAsync(client,
            $"companyId={seed.CompanyId}&search={Uri.EscapeDataString("historia ALFA")}");
        Assert.Equal(new[] { seed.PaymentYId, seed.PaymentXId }, Ids(byName));

        // Formatted CNPJ resolves to vendor B's digits-only document → payment Z.
        var byCnpj = await GetHistoryAsync(client,
            $"companyId={seed.CompanyId}&search={Uri.EscapeDataString(FormatCnpj(seed.VendorBDigits))}");
        Assert.Equal(new[] { seed.PaymentZId }, Ids(byCnpj));
    }

    [Fact]
    public async Task History_Hides_Reversed_Payments_Unless_IncludeReversed()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedWithPaymentsAsync(factory, client);

        var reverseRes = await client.PostAsJsonAsync(
            $"/api/payables/{seed.PayableBId}/payments/{seed.PaymentZId}/reverse",
            new { reason = "Baixa lançada em duplicidade" });
        Assert.Equal(HttpStatusCode.OK, reverseRes.StatusCode);

        // Default: reversed hidden.
        var hidden = await GetHistoryAsync(client, $"companyId={seed.CompanyId}");
        Assert.Equal(new[] { seed.PaymentYId, seed.PaymentXId }, Ids(hidden));

        // includeReversed=true: visible, flagged, with the reason.
        var shown = await GetHistoryAsync(client, $"companyId={seed.CompanyId}&includeReversed=true");
        Assert.Equal(new[] { seed.PaymentYId, seed.PaymentZId, seed.PaymentXId }, Ids(shown));
        var rowZ = shown.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("paymentId").GetString() == seed.PaymentZId);
        Assert.True(rowZ.GetProperty("isReversed").GetBoolean());
        Assert.Equal("Baixa lançada em duplicidade", rowZ.GetProperty("reversalReason").GetString());
    }

    [Fact]
    public async Task History_Is_Company_Scoped_And_Consolidated_Without_CompanyId()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();

        // Slim seeds: one payable + one payment per company.
        async Task<(string CompanyId, string PaymentId)> SeedOneAsync(string vendorName)
        {
            var companyId = await FinanceSeed.CreateCompanyAsync(client);
            var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, "CC-HSC", "Histórico Escopo");
            var vendorId = await FinanceSeed.CreateVendorAsync(client, vendorName);
            var bankId = await FinanceSeed.CreateBankAccountAsync(client, companyId, Random.Shared.Next(10_000_000, 99_999_999).ToString());
            var today = FinanceSeed.Today();
            var (payableId, installmentId) = await FinanceSeed.CreatePayableWithInstallmentAsync(
                factory, client, companyId, vendorId, costCenterId, today.AddDays(15), 800m);
            var paymentId = await PayAsync(client, payableId, installmentId, bankId, 800m, today, "Pix");
            return (companyId, paymentId);
        }

        var (companyA, paymentA) = await SeedOneAsync("Fornecedor Escopo A");
        var (_, paymentB) = await SeedOneAsync("Fornecedor Escopo B");

        var pageA = await GetHistoryAsync(client, $"companyId={companyA}");
        Assert.Contains(paymentA, Ids(pageA));
        Assert.DoesNotContain(paymentB, Ids(pageA));

        // AEL-906: admin (consolidated) may omit companyId → consolidated history (200, replaces the old 400).
        var all = await client.GetAsync("/api/payables/payments");
        Assert.Equal(HttpStatusCode.OK, all.StatusCode);
    }

    // AEL-906: a present companyId is subject to the opt-in filter even for a management role — a Finance user
    // granted ANOTHER company is masked from this one (mirrors the payment-queue-summary guard).
    [Fact]
    public async Task History_For_A_Company_Outside_The_Actor_Scope_Returns_403()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync();
        var companyId = await FinanceSeed.CreateCompanyAsync(admin);

        var outsider = factory.CreateClientWithScope(Roles.Finance, [Guid.NewGuid()], Permissions.PayablesRead);
        var forbidden = await outsider.GetAsync($"/api/payables/payments?companyId={companyId}");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal("payment.company_forbidden", (await ReadJson(forbidden)).GetProperty("error_code").GetString());
    }
}
