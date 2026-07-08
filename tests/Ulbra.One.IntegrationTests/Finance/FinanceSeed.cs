using System.Net.Http.Json;
using System.Text.Json;

namespace Ulbra.One.IntegrationTests.Finance;

/// <summary>Shared HTTP seeding helpers for Finance integration tests.</summary>
public static class FinanceSeed
{
    /// <summary>
    /// "Today" in Brazil local time (UTC-3), matching the API's ReceivableClock/business clock.
    /// Tests must use this (not DateTime.UtcNow) for receipt/payment dates: near UTC midnight the UTC
    /// date is a day ahead of the Brazil date, which the server rejects as a future-dated receipt.
    /// </summary>
    public static DateOnly Today() =>
        DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-3)).DateTime);

    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    public static async Task<string> CreateCompanyAsync(HttpClient client)
    {
        var cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        var res = await client.PostAsJsonAsync("/api/companies", new { legalName = "ULBRA Teste", cnpj });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("id").GetString()!;
    }

    public static async Task<string> CreateCostCenterAsync(HttpClient client, string companyId, string code, string name)
    {
        var res = await client.PostAsJsonAsync("/api/cost-centers",
            new { companyId, code, name, annualBudget = 1_000_000m, autoApprovalLimit = 1_000_000m, unitIds = Array.Empty<string>() });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("id").GetString()!;
    }

    /// <summary>Generates a valid (check-digit) CNPJ from a random 12-digit base.</summary>
    public static string RandomCnpj()
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

    public static async Task<string> CreateVendorAsync(HttpClient client, string companyId, string name)
        => await CreateVendorAsync(client, companyId, name, RandomCnpj());

    public static async Task<string> CreateVendorAsync(HttpClient client, string companyId, string name, string document)
    {
        var res = await client.PostAsJsonAsync("/api/vendors", new
        {
            companyId,
            personType = 1,
            name,
            tradeName = name,
            document,
            email = "contato@fs.com",
            phone = "5133334444",
            address = (object?)null,
            paymentTerms = "30 dias",
            bankInfo = (string?)null,
        });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("id").GetString()!;
    }

    public static async Task<string> CreateBankAccountAsync(HttpClient client, string companyId, string accountNumber)
    {
        var res = await client.PostAsJsonAsync("/api/bank-accounts", new
        {
            companyId,
            bankCode = "033",
            bankName = "Banco Santander",
            agency = "1234",
            accountNumber,
            accountDigit = "6",
            accountType = "Checking",
            alias = "Conta " + accountNumber,
            openingBalance = 1_000_000m,
        });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("id").GetString()!;
    }

    /// <summary>
    /// Creates a single-installment payable and (optionally) approves it so it reaches <c>Scheduled</c>.
    /// Returns the created payable id.
    /// </summary>
    public static async Task<string> CreatePayableAsync(
        HttpClient client, string companyId, string vendorId, string costCenterId, DateOnly dueDate, decimal amount, bool approve = true)
    {
        var createRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId,
            vendorId,
            costCenterId,
            description = "Para pagar",
            installments = new[] { new { dueDate = dueDate.ToString("yyyy-MM-dd"), amount } },
            type = "Product", // determined product (no source withholding) — required before a payable can schedule
        });
        createRes.EnsureSuccessStatusCode();
        var body = await ReadJson(createRes);
        var payableId = body.GetProperty("id").GetString()!;

        if (approve)
        {
            var approveRes = await client.PostAsJsonAsync($"/api/payables/{payableId}/approve", new { });
            approveRes.EnsureSuccessStatusCode();
        }

        return payableId;
    }

    /// <summary>
    /// Creates a single-installment payable and (optionally) approves it. Returns (payableId, installmentId).
    /// </summary>
    public static async Task<(string PayableId, string InstallmentId)> CreatePayableWithInstallmentAsync(
        HttpClient client, string companyId, string vendorId, string costCenterId, DateOnly dueDate, decimal amount, bool approve = true)
    {
        var createRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId,
            vendorId,
            costCenterId,
            description = "Para pagar",
            installments = new[] { new { dueDate = dueDate.ToString("yyyy-MM-dd"), amount } },
            type = "Product", // determined product (no source withholding) — required before a payable can schedule
        });
        createRes.EnsureSuccessStatusCode();
        var body = await ReadJson(createRes);
        var payableId = body.GetProperty("id").GetString()!;
        var installmentId = body.GetProperty("installments")[0].GetProperty("id").GetString()!;

        if (approve)
        {
            var approveRes = await client.PostAsJsonAsync($"/api/payables/{payableId}/approve", new { });
            approveRes.EnsureSuccessStatusCode();
        }

        return (payableId, installmentId);
    }

    public static async Task RegisterPaymentAsync(HttpClient client, string payableId, string installmentId, string bankId, decimal amount, DateOnly date)
    {
        var res = await client.PostAsJsonAsync($"/api/payables/{payableId}/installments/{installmentId}/payments", new
        {
            paymentDate = date.ToString("yyyy-MM-dd"),
            amount,
            bankAccountId = bankId,
            method = "Pix",
        });
        res.EnsureSuccessStatusCode();
    }

    public static async Task<string> CreateCustomerAsync(HttpClient client, string companyId, string name)
    {
        var res = await client.PostAsJsonAsync("/api/customers", new
        {
            companyId,
            personType = 1,
            name,
            tradeName = name,
            document = RandomCnpj(),
            email = "financeiro@hu.com",
            phone = "5133334444",
            address = (object?)null,
            creditLimit = (decimal?)null,
        });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("id").GetString()!;
    }

    /// <summary>
    /// Creates a single-installment receivable. Returns (receivableId, firstInstallmentId).
    /// </summary>
    public static async Task<(string ReceivableId, string InstallmentId)> CreateReceivableAsync(
        HttpClient client, string companyId, string payerId, string costCenterId, DateOnly dueDate, decimal amount)
    {
        var createRes = await client.PostAsJsonAsync("/api/receivables", new
        {
            companyId,
            payerId,
            costCenterId,
            description = "Mensalidade",
            installments = new[] { new { dueDate = dueDate.ToString("yyyy-MM-dd"), amount } },
        });
        createRes.EnsureSuccessStatusCode();
        var body = await ReadJson(createRes);
        var receivableId = body.GetProperty("id").GetString()!;

        var detail = await ReadJson(await client.GetAsync($"/api/receivables/{receivableId}"));
        var installmentId = detail.GetProperty("installments")[0].GetProperty("id").GetString()!;

        return (receivableId, installmentId);
    }

    public static async Task RegisterReceiptAsync(HttpClient client, string receivableId, string installmentId, string bankId, decimal amount, DateOnly date)
    {
        var res = await client.PostAsJsonAsync($"/api/receivables/{receivableId}/installments/{installmentId}/receipts", new
        {
            receiptDate = date.ToString("yyyy-MM-dd"),
            amount,
            bankAccountId = bankId,
            method = "Pix",
        });
        res.EnsureSuccessStatusCode();
    }

    private sealed record AccountRef(string Id, string Code, string Kind);

    /// <summary>
    /// Returns an analytical account id from the global ledger chart (auto-seeded on startup).
    /// Used to classify a payable in tests.
    /// </summary>
    public static async Task<string> CreateAccountingAccountAsync(HttpClient client)
    {
        var accounts = await client.GetFromJsonAsync<List<AccountRef>>(
            "/api/ledger-accounts?kind=Analytical&activeOnly=true");
        return accounts!.First().Id;
    }
}
