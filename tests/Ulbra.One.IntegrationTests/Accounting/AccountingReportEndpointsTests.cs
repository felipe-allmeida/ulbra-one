using System.Net.Http.Json;
using System.Text.Json;

namespace Ulbra.One.IntegrationTests.Accounting;

[Collection("Postgres")]
public class AccountingReportEndpointsTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private static async Task<string> SeedCompanyAsync(HttpClient client)
    {
        var cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        var res = await client.PostAsJsonAsync("/api/companies", new { legalName = "ULBRA Relatórios Teste", cnpj });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("id").GetString()!;
    }

    private static async Task<Dictionary<string, string>> SeedChartAsync(HttpClient client, string companyId)
    {
        var seedRes = await client.PostAsJsonAsync("/api/ledger-accounts/seed", new { companyId });
        seedRes.EnsureSuccessStatusCode();
        var accounts = await ReadJson(seedRes);
        return accounts.EnumerateArray()
            .ToDictionary(a => a.GetProperty("code").GetString()!, a => a.GetProperty("id").GetString()!);
    }

    private static object Line(string accountId, string side, decimal amount) => new
    {
        ledgerAccountId = accountId, side, amount,
    };

    /// <summary>Creates a balanced 2-line entry and posts it.</summary>
    private static async Task PostEntryAsync(HttpClient client, string companyId, string competence,
        string debitCode, string creditCode, Dictionary<string, string> acc, decimal amount)
    {
        var createRes = await client.PostAsJsonAsync("/api/journal-entries", new
        {
            companyId,
            competenceDate = competence,
            description = $"{debitCode} → {creditCode}",
            lines = new[] { Line(acc[debitCode], "Debit", amount), Line(acc[creditCode], "Credit", amount) },
        });
        createRes.EnsureSuccessStatusCode();
        var id = (await ReadJson(createRes)).GetProperty("id").GetString()!;
        (await client.PostAsync($"/api/journal-entries/{id}/post", null)).EnsureSuccessStatusCode();
    }

    /// <summary>Posts a self-contained company books: capital in, revenue, payroll and energy expense.</summary>
    private static async Task<string> SeedBooksAsync(HttpClient client)
    {
        var companyId = await SeedCompanyAsync(client);
        var acc = await SeedChartAsync(client, companyId);
        // AELBRA analytical codes (Asset / Equity / Revenue / Expense / Liability).
        await PostEntryAsync(client, companyId, "2026-01-02", "1.2.05.05.020", "2.6.05.05.020", acc, 100_000m); // capital
        await PostEntryAsync(client, companyId, "2026-01-10", "1.2.05.15.015", "3.2.05.10.020", acc, 50_000m);  // revenue
        await PostEntryAsync(client, companyId, "2026-01-20", "4.2.05.05.020", "1.2.05.15.015", acc, 30_000m);  // payroll
        await PostEntryAsync(client, companyId, "2026-01-25", "4.2.15.05.020", "2.2.05.15.080", acc, 8_000m);   // energy
        return companyId;
    }

    [Fact]
    public async Task TrialBalance_Debit_Equals_Credit()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedBooksAsync(client);

        var tb = await ReadJson(await client.GetAsync($"/api/accounting/trial-balance?companyId={companyId}"));
        Assert.True(tb.GetProperty("isBalanced").GetBoolean());
        Assert.Equal(tb.GetProperty("totalDebit").GetDecimal(), tb.GetProperty("totalCredit").GetDecimal());
        Assert.True(tb.GetProperty("totalDebit").GetDecimal() > 0);
    }

    [Fact]
    public async Task IncomeStatement_Computes_Result()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedBooksAsync(client);

        var dre = await ReadJson(await client.GetAsync(
            $"/api/accounting/income-statement?companyId={companyId}&from=2026-01-01&to=2026-12-31"));
        Assert.Equal(50_000m, dre.GetProperty("totalRevenue").GetDecimal());
        Assert.Equal(38_000m, dre.GetProperty("totalExpense").GetDecimal());
        Assert.Equal(12_000m, dre.GetProperty("result").GetDecimal());
    }

    [Fact]
    public async Task BalanceSheet_Ativo_Equals_Passivo_Plus_PL()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedBooksAsync(client);

        var bp = await ReadJson(await client.GetAsync($"/api/accounting/balance-sheet?companyId={companyId}&asOf=2026-12-31"));
        Assert.True(bp.GetProperty("isBalanced").GetBoolean());
        var assets = bp.GetProperty("totalAssets").GetDecimal();
        var liabilities = bp.GetProperty("totalLiabilities").GetDecimal();
        var equity = bp.GetProperty("totalEquity").GetDecimal();
        Assert.Equal(assets, liabilities + equity);
        // Period result folds into PL: revenue 50k - expense 38k = 12k.
        Assert.Equal(12_000m, bp.GetProperty("periodResult").GetDecimal());
    }

    [Fact]
    public async Task Consolidated_Aggregates_Across_Companies()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var companyA = await SeedBooksAsync(client);
        var companyB = await SeedBooksAsync(client);

        var perA = await ReadJson(await client.GetAsync($"/api/accounting/trial-balance?companyId={companyA}"));
        var perB = await ReadJson(await client.GetAsync($"/api/accounting/trial-balance?companyId={companyB}"));
        var consolidated = await ReadJson(await client.GetAsync("/api/accounting/trial-balance"));

        // Consolidated (no companyId) must be balanced and at least the sum of the two companies' debits.
        Assert.True(consolidated.GetProperty("isBalanced").GetBoolean());
        var consolidatedDebit = consolidated.GetProperty("totalDebit").GetDecimal();
        Assert.True(consolidatedDebit >= perA.GetProperty("totalDebit").GetDecimal() + perB.GetProperty("totalDebit").GetDecimal());

        // Per-company view is scoped — equity is the same per company but consolidated is larger.
        Assert.True(consolidatedDebit > perA.GetProperty("totalDebit").GetDecimal());
    }
}
