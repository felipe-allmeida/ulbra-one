using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ulbra.One.IntegrationTests.Finance;

[Collection("Postgres")]
public class CashFlowEndpointsTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    // ---- shared seed helpers (mirror aging/receivable tests) ----------------------------------

    private static async Task<string> CreateCompanyAsync(HttpClient client)
    {
        var res = await client.PostAsJsonAsync("/api/companies", new { legalName = "ULBRA Teste", cnpj = RandomCnpj() });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("id").GetString()!;
    }

    private static async Task<string> CreateCostCenterAsync(HttpClient client, string companyId, string code, string name)
    {
        var res = await client.PostAsJsonAsync("/api/cost-centers",
            new { companyId, code, name, annualBudget = 1_000_000m, autoApprovalLimit = 1_000_000m, unitIds = Array.Empty<string>() });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("id").GetString()!;
    }

    private static string RandomCnpj()
    {
        var digits = new int[14];
        for (var i = 0; i < 12; i++) digits[i] = Random.Shared.Next(0, 10);
        int[] w1 = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        int[] w2 = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        digits[12] = Dv(digits, w1);
        digits[13] = Dv(digits, w2);
        return string.Concat(digits);

        static int Dv(int[] d, int[] w)
        {
            var sum = 0;
            for (var i = 0; i < w.Length; i++) sum += d[i] * w[i];
            var rest = sum % 11;
            return rest < 2 ? 0 : 11 - rest;
        }
    }

    private static async Task<string> CreateVendorAsync(HttpClient client, string companyId, string name)
    {
        var res = await client.PostAsJsonAsync("/api/vendors", new
        {
            companyId, personType = 1, name, tradeName = name, document = RandomCnpj(),
            email = "contato@fs.com", phone = "5133334444", address = (object?)null,
            paymentTerms = "30 dias", bankInfo = (string?)null,
        });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("id").GetString()!;
    }

    private static async Task<string> CreateCustomerAsync(HttpClient client, string companyId, string name)
    {
        var res = await client.PostAsJsonAsync("/api/customers", new
        {
            companyId, personType = 1, name, tradeName = name, document = RandomCnpj(),
            email = "fin@c.com", phone = "5133334444", address = (object?)null, creditLimit = (decimal?)null,
        });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("id").GetString()!;
    }

    private static async Task<string> CreateBankAccountAsync(HttpClient client, string companyId, string accountNumber, decimal opening)
    {
        var res = await client.PostAsJsonAsync("/api/bank-accounts", new
        {
            companyId, bankCode = "033", bankName = "Banco Santander", agency = "1234",
            accountNumber, accountDigit = "6", accountType = "Checking",
            alias = "Conta " + accountNumber, openingBalance = opening,
        });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("id").GetString()!;
    }

    private static async Task<string> CreatePayableAsync(
        HttpClient client, string companyId, string vendorId, string costCenterId, DateOnly dueDate, decimal amount, bool approve = true)
    {
        var createRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId, vendorId, costCenterId, description = "Para pagar",
            installments = new[] { new { dueDate = dueDate.ToString("yyyy-MM-dd"), amount } },
        });
        createRes.EnsureSuccessStatusCode();
        var payableId = (await ReadJson(createRes)).GetProperty("id").GetString()!;
        if (approve)
            (await client.PostAsJsonAsync($"/api/payables/{payableId}/approve", new { })).EnsureSuccessStatusCode();
        return payableId;
    }

    private static async Task CancelPayableAsync(HttpClient client, string payableId) =>
        (await client.PostAsJsonAsync($"/api/payables/{payableId}/cancel", new { reason = "x" })).EnsureSuccessStatusCode();

    private static async Task<string> CreateReceivableAsync(
        HttpClient client, string companyId, string payerId, string costCenterId, DateOnly dueDate, decimal amount)
    {
        var res = await client.PostAsJsonAsync("/api/receivables", new
        {
            companyId, payerId, costCenterId, description = "A receber",
            installments = new[] { new { dueDate = dueDate.ToString("yyyy-MM-dd"), amount } },
        });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("id").GetString()!;
    }

    // ---- tests -------------------------------------------------------------------------------

    [Fact]
    public async Task GetCashFlow_PerCompany_Projects_Forecast_And_OverdueReceivable()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var company = await CreateCompanyAsync(client);
        var cc = await CreateCostCenterAsync(client, company, "CC1", "Geral");
        var vendor = await CreateVendorAsync(client, company, "Fornecedor A");
        var customer = await CreateCustomerAsync(client, company, "Cliente A");
        await CreateBankAccountAsync(client, company, "00012345", 50_000m);

        // Future outflow next month; overdue outflow last month (folds into current period).
        await CreatePayableAsync(client, company, vendor, cc, today.AddMonths(1), 10_000m);
        await CreatePayableAsync(client, company, vendor, cc, today.AddMonths(-1), 3_000m);
        // Future inflow next month; overdue inflow last month (excluded -> overdueReceivable line).
        await CreateReceivableAsync(client, company, customer, cc, today.AddMonths(1), 8_000m);
        await CreateReceivableAsync(client, company, customer, cc, today.AddMonths(-1), 1_500m);

        var res = await client.GetAsync($"/api/cash-flow?companyId={company}&granularity=Month&horizon=3&asOf={today:yyyy-MM-dd}&minReserve=0");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await ReadJson(res);

        Assert.Equal(50_000m, body.GetProperty("startingBalance").GetDecimal()); // openingBalance fallback (no anchor)
        Assert.Equal(1_500m, body.GetProperty("overdueReceivable").GetDecimal());
        Assert.Equal("BRL", body.GetProperty("currency").GetString());

        var periods = body.GetProperty("periods");
        Assert.Equal(3, periods.GetArrayLength());
        // Period 0 (current): overdue payable 3000 folded in, no inflow forecast.
        Assert.Equal(3_000m, periods[0].GetProperty("outflowForecast").GetDecimal());
        Assert.Equal(0m, periods[0].GetProperty("inflowForecast").GetDecimal());
        // Period 1 (next month): the future inflow 8000 and outflow 10000.
        Assert.Equal(8_000m, periods[1].GetProperty("inflowForecast").GetDecimal());
        Assert.Equal(10_000m, periods[1].GetProperty("outflowForecast").GetDecimal());

        // KPIs: net = inflow(8000) - outflow(13000) = -5000.
        Assert.Equal(-5_000m, body.GetProperty("kpis").GetProperty("netResult").GetDecimal());
    }

    [Fact]
    public async Task GetCashFlow_Excludes_Cancelled_Includes_PendingApproval()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var company = await CreateCompanyAsync(client);
        var cc = await CreateCostCenterAsync(client, company, "CC1", "Geral");
        var vendor = await CreateVendorAsync(client, company, "Fornecedor A");

        // Cancelled payable -> excluded.
        var cancelled = await CreatePayableAsync(client, company, vendor, cc, today.AddMonths(1), 9_999m, approve: false);
        await CancelPayableAsync(client, cancelled);
        // Pending-approval (not approved) payable -> included.
        await CreatePayableAsync(client, company, vendor, cc, today.AddMonths(1), 400m, approve: false);

        var body = await ReadJson(await client.GetAsync(
            $"/api/cash-flow?companyId={company}&granularity=Month&horizon=3&asOf={today:yyyy-MM-dd}"));
        Assert.Equal(400m, body.GetProperty("kpis").GetProperty("totalOutflowForecast").GetDecimal());
    }

    [Fact]
    public async Task GetCashFlow_Consolidated_Sums_All_Companies()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var companyA = await CreateCompanyAsync(client);
        var ccA = await CreateCostCenterAsync(client, companyA, "CC1", "Geral");
        var vendorA = await CreateVendorAsync(client, companyA, "Fornecedor A");
        await CreateBankAccountAsync(client, companyA, "00010001", 20_000m);
        await CreatePayableAsync(client, companyA, vendorA, ccA, today.AddMonths(1), 1_000m);

        var companyB = await CreateCompanyAsync(client);
        var ccB = await CreateCostCenterAsync(client, companyB, "CC1", "Geral");
        var vendorB = await CreateVendorAsync(client, companyB, "Fornecedor B");
        await CreateBankAccountAsync(client, companyB, "00020002", 30_000m);
        await CreatePayableAsync(client, companyB, vendorB, ccB, today.AddMonths(1), 2_000m);

        // Per-company A
        var perA = await ReadJson(await client.GetAsync($"/api/cash-flow?companyId={companyA}&asOf={today:yyyy-MM-dd}"));
        Assert.Equal(20_000m, perA.GetProperty("startingBalance").GetDecimal());
        Assert.Equal(1_000m, perA.GetProperty("kpis").GetProperty("totalOutflowForecast").GetDecimal());

        // Consolidated (no companyId): at least A+B starting balances and outflows.
        var all = await ReadJson(await client.GetAsync($"/api/cash-flow?asOf={today:yyyy-MM-dd}"));
        Assert.True(all.GetProperty("startingBalance").GetDecimal() >= 50_000m);
        Assert.True(all.GetProperty("kpis").GetProperty("totalOutflowForecast").GetDecimal() >= 3_000m);
    }

    [Fact]
    public async Task GetCashFlowItems_Drill_Sums_To_Period_Bucket()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var company = await CreateCompanyAsync(client);
        var cc = await CreateCostCenterAsync(client, company, "CC1", "Geral");
        var vendor = await CreateVendorAsync(client, company, "Fornecedor A");
        await CreatePayableAsync(client, company, vendor, cc, today.AddMonths(1), 4_000m);
        await CreatePayableAsync(client, company, vendor, cc, today.AddMonths(1), 1_500m);

        var body = await ReadJson(await client.GetAsync(
            $"/api/cash-flow?companyId={company}&granularity=Month&horizon=3&asOf={today:yyyy-MM-dd}"));
        var period1 = body.GetProperty("periods")[1];
        var bucket = period1.GetProperty("outflowForecast").GetDecimal();
        Assert.Equal(5_500m, bucket);

        var from = period1.GetProperty("start").GetString();
        var to = period1.GetProperty("end").GetString();
        var items = await ReadJson(await client.GetAsync(
            $"/api/cash-flow/items?companyId={company}&from={from}&to={to}&direction=out&asOf={today:yyyy-MM-dd}"));

        Assert.Equal(2, items.GetArrayLength());
        var sum = items.EnumerateArray().Sum(i => i.GetProperty("amount").GetDecimal());
        Assert.Equal(bucket, sum);
        foreach (var it in items.EnumerateArray())
            Assert.Equal("payable", it.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task GetCashFlowItems_Current_Period_Drill_Includes_Overdue_Payables()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var company = await CreateCompanyAsync(client);
        var cc = await CreateCostCenterAsync(client, company, "CC1", "Geral");
        var vendor = await CreateVendorAsync(client, company, "Fornecedor A");
        await CreatePayableAsync(client, company, vendor, cc, today.AddMonths(-2), 700m); // overdue
        await CreatePayableAsync(client, company, vendor, cc, today, 200m);               // due today

        var body = await ReadJson(await client.GetAsync(
            $"/api/cash-flow?companyId={company}&granularity=Month&horizon=3&asOf={today:yyyy-MM-dd}"));
        var period0 = body.GetProperty("periods")[0];
        var bucket = period0.GetProperty("outflowForecast").GetDecimal();
        Assert.Equal(900m, bucket); // overdue folds into current period

        var from = period0.GetProperty("start").GetString();
        var to = period0.GetProperty("end").GetString();
        var items = await ReadJson(await client.GetAsync(
            $"/api/cash-flow/items?companyId={company}&from={from}&to={to}&direction=out&asOf={today:yyyy-MM-dd}"));
        var sum = items.EnumerateArray().Sum(i => i.GetProperty("amount").GetDecimal());
        Assert.Equal(900m, sum);
    }

    [Fact]
    public async Task SetReconciledBalance_Persists_And_Recomputes_CurrentBalance()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var company = await CreateCompanyAsync(client);
        var bankId = await CreateBankAccountAsync(client, company, "00012345", 1_000m);

        var asOf = today.AddDays(-3);
        var put = await client.PutAsJsonAsync($"/api/bank-accounts/{bankId}/reconciled-balance",
            new { balance = 5_000m, asOf = asOf.ToString("yyyy-MM-dd") });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var putBody = await ReadJson(put);
        Assert.Equal(5_000m, putBody.GetProperty("reconciledBalance").GetDecimal());
        Assert.Equal(5_000m, putBody.GetProperty("currentBalance").GetDecimal()); // no movements after anchor

        // The report now uses the anchor (5000) as the starting balance, not openingBalance (1000).
        var report = await ReadJson(await client.GetAsync($"/api/cash-flow?companyId={company}&asOf={today:yyyy-MM-dd}"));
        Assert.Equal(5_000m, report.GetProperty("startingBalance").GetDecimal());
        var account = report.GetProperty("accounts")[0];
        Assert.Equal(5_000m, account.GetProperty("currentBalance").GetDecimal());
        Assert.Equal(asOf.ToString("yyyy-MM-dd"), account.GetProperty("reconciledAt").GetString());
    }
}
