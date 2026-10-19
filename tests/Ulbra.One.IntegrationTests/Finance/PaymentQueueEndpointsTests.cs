using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ulbra.One.Common.Application.Authentication; // Permissions
using Ulbra.One.Identity.Domain; // Roles

namespace Ulbra.One.IntegrationTests.Finance;

/// <summary>
/// Covers the payment-queue additions to GET /api/payables (search + paymentMethod filter + the
/// appended response fields) and the new GET /api/payables/payment-queue-summary endpoint.
/// </summary>
[Collection("Postgres")]
public class PaymentQueueEndpointsTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private static string FormatCnpj(string digits) =>
        $"{digits[..2]}.{digits[2..5]}.{digits[5..8]}/{digits[8..12]}-{digits[12..]}";

    private sealed record Seed(string CompanyId, string CostCenterId);

    private static async Task<Seed> SeedCompanyAsync(HttpClient client, string ccCode)
    {
        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, ccCode, "Fila de Pagamentos");
        return new Seed(companyId, costCenterId);
    }

    /// <summary>Creates a payable (left at Received — the queue filters are payable-level, no approval needed).</summary>
    private static async Task<string> CreatePayableAsync(
        HttpClient client, Seed seed, string vendorId, string description,
        string? documentNumber = null, string? issueDate = null, string? unitId = null,
        string? paymentMethod = null, string? boletoDigitableLine = null,
        string dueDate = "2026-09-10", decimal amount = 1_000m)
    {
        var res = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId,
            costCenterId = seed.CostCenterId,
            unitId,
            description,
            documentNumber,
            issueDate,
            paymentMethod,
            installments = new[] { new { dueDate, amount, boletoDigitableLine } },
        });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("id").GetString()!;
    }

    /// <summary>Creates the company's default paying bank account with the given default method.</summary>
    private static async Task<List<JsonElement>> ListRowsAsync(HttpClient client, string query)
    {
        var res = await client.GetAsync($"/api/payables?{query}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await ReadJson(res)).GetProperty("items").EnumerateArray().ToList();
    }

    // ---------------------------------------------------------------------------
    // List: search
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task List_Search_By_Counterparty_Substring_Is_CaseInsensitive()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedCompanyAsync(client, "CC-PQ1");

        var vendorAlpha = await FinanceSeed.CreateVendorAsync(client, "Alpha Consultoria Ltda");
        var vendorBeta = await FinanceSeed.CreateVendorAsync(client, "Beta Materiais SA");
        var alphaPayable = await CreatePayableAsync(client, seed, vendorAlpha, "Serviço Alpha");
        await CreatePayableAsync(client, seed, vendorBeta, "Compra Beta");

        // Uppercase query, partial word — must match "Alpha Consultoria Ltda" case-insensitively.
        var rows = await ListRowsAsync(client,
            $"companyId={seed.CompanyId}&search={Uri.EscapeDataString("ALPHA CONSULT")}&pageSize=100");

        var row = Assert.Single(rows);
        Assert.Equal(alphaPayable, row.GetProperty("payableId").GetString());
        Assert.Equal("Alpha Consultoria Ltda", row.GetProperty("counterpartyName").GetString());
    }

    [Fact]
    public async Task List_Search_By_Formatted_Cnpj_Matches_DigitsOnly_Vendor_Document()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedCompanyAsync(client, "CC-PQ2");

        var digits = FinanceSeed.RandomCnpj();
        var vendorTarget = await FinanceSeed.CreateVendorAsync(client, "Fornecedor CNPJ Alvo", digits);
        var vendorOther = await FinanceSeed.CreateVendorAsync(client, "Fornecedor CNPJ Outro");
        var targetPayable = await CreatePayableAsync(client, seed, vendorTarget, "Compra do alvo");
        await CreatePayableAsync(client, seed, vendorOther, "Compra do outro");

        // Vendor.Document is stored digits-only; the formatted query must still hit it.
        var rows = await ListRowsAsync(client,
            $"companyId={seed.CompanyId}&search={Uri.EscapeDataString(FormatCnpj(digits))}&pageSize=100");

        var row = Assert.Single(rows);
        Assert.Equal(targetPayable, row.GetProperty("payableId").GetString());
        Assert.Equal(digits, row.GetProperty("vendorDocument").GetString());
    }

    [Fact]
    public async Task List_Search_By_DocumentNumber_Is_CaseInsensitive()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedCompanyAsync(client, "CC-PQ3");
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Fornecedor NF Search");

        var withNf = await CreatePayableAsync(client, seed, vendorId, "Com NF", documentNumber: "NF-778899");
        await CreatePayableAsync(client, seed, vendorId, "Sem NF");

        var rows = await ListRowsAsync(client, $"companyId={seed.CompanyId}&search=nf-7788&pageSize=100");

        var row = Assert.Single(rows);
        Assert.Equal(withNf, row.GetProperty("payableId").GetString());
        Assert.Equal("NF-778899", row.GetProperty("documentNumber").GetString());
    }

    // ---------------------------------------------------------------------------
    // List: paymentMethod filter (EFFECTIVE method)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task List_PaymentMethod_Filter_Matches_Effective_Installment_Method_And_Excludes_Others()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedCompanyAsync(client, "CC-PQ4");
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Metodo");

        var explicitTed = await CreatePayableAsync(client, seed, vendorId, "TED explícito", paymentMethod: "Ted");
        // AEL-933: sem forma na parcela/payable e sem default de conta → não casa nenhum filtro de forma.
        await CreatePayableAsync(client, seed, vendorId, "Sem método");
        var explicitPix = await CreatePayableAsync(client, seed, vendorId, "PIX explícito", paymentMethod: "Pix");

        // Ted filter → só o TED explícito.
        var tedRows = await ListRowsAsync(client, $"companyId={seed.CompanyId}&paymentMethod=Ted&pageSize=100");
        var tedRow = Assert.Single(tedRows);
        Assert.Equal(explicitTed, tedRow.GetProperty("payableId").GetString());
        Assert.Equal("Ted", tedRow.GetProperty("effectiveMethod").GetString());

        // Pix filter → só o PIX explícito.
        var pixRows = await ListRowsAsync(client, $"companyId={seed.CompanyId}&paymentMethod=Pix&pageSize=100");
        var pixRow = Assert.Single(pixRows);
        Assert.Equal(explicitPix, pixRow.GetProperty("payableId").GetString());
        Assert.Equal("Pix", pixRow.GetProperty("effectiveMethod").GetString());
    }

    // ---------------------------------------------------------------------------
    // List: appended response fields
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task List_Populates_The_Appended_PaymentQueue_Fields()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedCompanyAsync(client, "CC-PQ5");
        var unitId = await FinanceSeed.CreateUnitAsync(client, seed.CompanyId, "Reitoria", "REI");
        var digits = FinanceSeed.RandomCnpj();
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Campos Novos", digits);

        const string boletoLine = "23793381286000782713695000063305975520000150000";
        var payableId = await CreatePayableAsync(client, seed, vendorId, "Boleto com campos novos",
            documentNumber: "NF-1234", issueDate: "2026-07-15", unitId: unitId, boletoDigitableLine: boletoLine,
            paymentMethod: "Boleto");

        var rows = await ListRowsAsync(client, $"companyId={seed.CompanyId}&pageSize=100");
        var row = rows.Single(r => r.GetProperty("payableId").GetString() == payableId);

        Assert.Equal(digits, row.GetProperty("vendorDocument").GetString());
        Assert.Equal(vendorId, row.GetProperty("vendorId").GetString());
        // Effective method comes from the payable's forma (AEL-933) — Boleto here.
        Assert.Equal("Boleto", row.GetProperty("effectiveMethod").GetString());
        Assert.Equal(boletoLine, row.GetProperty("boletoDigitableLine").GetString());
        Assert.Equal("NF-1234", row.GetProperty("documentNumber").GetString());
        Assert.Equal("2026-07-15", row.GetProperty("issueDate").GetString());
        Assert.Equal(unitId, row.GetProperty("originUnitId").GetString());
        Assert.False(row.GetProperty("hasFiscalDocument").GetBoolean()); // no NF registered
    }

    // ---------------------------------------------------------------------------
    // Summary
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Summary_Folds_PastDue_Into_Overdue_And_Groups_Remaining_By_Day()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedCompanyAsync(client, "CC-PQ6");
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Vencimentos");

        // The endpoint buckets by UTC today (TimeProvider) — seed relative to DateTime.UtcNow.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await FinanceSeed.CreatePayableWithInstallmentAsync(factory, client, seed.CompanyId, vendorId, seed.CostCenterId, today.AddDays(-1), 100m);
        await FinanceSeed.CreatePayableWithInstallmentAsync(factory, client, seed.CompanyId, vendorId, seed.CostCenterId, today, 200m);
        await FinanceSeed.CreatePayableWithInstallmentAsync(factory, client, seed.CompanyId, vendorId, seed.CostCenterId, today.AddDays(1), 300m);

        var res = await client.GetAsync(
            $"/api/payables/payment-queue-summary?companyId={seed.CompanyId}&status=Scheduled&paid=false");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await ReadJson(res);

        Assert.Equal(today.ToString("yyyy-MM-dd"), body.GetProperty("asOf").GetString());
        Assert.Equal(1, body.GetProperty("overdueCount").GetInt32());
        Assert.Equal(100m, body.GetProperty("overdueNetTotal").GetDecimal());

        var groups = body.GetProperty("groups").EnumerateArray().ToList();
        Assert.Equal(2, groups.Count);
        // Today is the FIRST group ("Vence hoje"), never folded into Overdue.
        Assert.Equal(today.ToString("yyyy-MM-dd"), groups[0].GetProperty("dueDate").GetString());
        Assert.Equal(1, groups[0].GetProperty("count").GetInt32());
        Assert.Equal(200m, groups[0].GetProperty("netTotal").GetDecimal());
        Assert.Equal(today.AddDays(1).ToString("yyyy-MM-dd"), groups[1].GetProperty("dueDate").GetString());
        Assert.Equal(1, groups[1].GetProperty("count").GetInt32());
        Assert.Equal(300m, groups[1].GetProperty("netTotal").GetDecimal());

        // No withholdings → NetAmount == Amount, so the totals are the plain sums.
        Assert.Equal(3, body.GetProperty("totalCount").GetInt32());
        Assert.Equal(600m, body.GetProperty("totalNetTotal").GetDecimal());
    }

    [Fact]
    public async Task Summary_Total_Agrees_With_The_List_Under_The_Same_Filters()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedCompanyAsync(client, "CC-PQ7");
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Conferência");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await FinanceSeed.CreatePayableWithInstallmentAsync(factory, client, seed.CompanyId, vendorId, seed.CostCenterId, today.AddDays(-2), 150m);
        await FinanceSeed.CreatePayableWithInstallmentAsync(factory, client, seed.CompanyId, vendorId, seed.CostCenterId, today, 250m);
        await FinanceSeed.CreatePayableWithInstallmentAsync(factory, client, seed.CompanyId, vendorId, seed.CostCenterId, today.AddDays(3), 350m);
        // A non-Scheduled payable that both queries must ignore.
        await CreatePayableAsync(client, seed, vendorId, "Ainda Received");

        const string filters = "status=Scheduled&paid=false";
        var summary = await ReadJson(await client.GetAsync(
            $"/api/payables/payment-queue-summary?companyId={seed.CompanyId}&{filters}"));
        var list = await ReadJson(await client.GetAsync(
            $"/api/payables?companyId={seed.CompanyId}&{filters}&pageSize=500"));

        Assert.Equal(list.GetProperty("total").GetInt32(), summary.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, summary.GetProperty("totalCount").GetInt32());
        var listNetSum = list.GetProperty("items").EnumerateArray().Sum(r => r.GetProperty("netAmount").GetDecimal());
        Assert.Equal(listNetSum, summary.GetProperty("totalNetTotal").GetDecimal());
    }

    [Fact]
    public async Task Summary_Without_CompanyId_Is_Consolidated_For_Management_And_403_Otherwise()
    {
        var factory = fixture.Api;
        // AEL-906: a consolidated role may omit companyId → consolidated summary (200, replaces the old 400).
        var admin = await factory.CreateAdminClientAsync();
        var ok = await admin.GetAsync("/api/payables/payment-queue-summary");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        // A non-consolidated role still can't sweep every company: omitted companyId → 403.
        var scoped = factory.CreateClientWith(Roles.User, Permissions.PayablesRead);
        var forbidden = await scoped.GetAsync("/api/payables/payment-queue-summary");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal("payable.consolidated_forbidden", (await ReadJson(forbidden)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Summary_For_Company_Outside_The_Actor_Scope_Returns_403_CompanyForbidden()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync();
        var seed = await SeedCompanyAsync(admin, "CC-PQ8");
        var companyGuid = Guid.Parse(seed.CompanyId[(seed.CompanyId.LastIndexOf('_') + 1)..]);

        // Actor has payables:read but is scoped to ANOTHER company → fail-closed 403 (AEL-764 mask).
        var outsider = factory.CreateClientWithScope(Roles.Finance, [Guid.NewGuid()], Permissions.PayablesRead);
        var forbidden = await outsider.GetAsync($"/api/payables/payment-queue-summary?companyId={seed.CompanyId}");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal("payable.company_forbidden", (await ReadJson(forbidden)).GetProperty("error_code").GetString());

        // Control: the same role scoped to the right company gets through.
        var insider = factory.CreateClientWithScope(Roles.Finance, [companyGuid], Permissions.PayablesRead);
        var ok = await insider.GetAsync($"/api/payables/payment-queue-summary?companyId={seed.CompanyId}");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }
}
