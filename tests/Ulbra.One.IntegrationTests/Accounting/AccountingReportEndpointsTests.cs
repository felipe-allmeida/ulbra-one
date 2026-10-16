using System.Net.Http.Json;
using System.Text.Json;
using Ulbra.One.IntegrationTests;

namespace Ulbra.One.IntegrationTests.Accounting;

[Collection("Postgres")]
public class AccountingReportEndpointsTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private static async Task<string> SeedCompanyAsync(HttpClient client)
    {
        var cnpj = TestDocuments.RandomCnpj();
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

    /// <summary>Creates a balanced 2-line entry, posts it and returns its id.</summary>
    private static async Task<string> PostEntryAsync(HttpClient client, string companyId, string competence,
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
        return id;
    }

    private static async Task ReverseEntryAsync(HttpClient client, string entryId, string competence) =>
        (await client.PostAsJsonAsync($"/api/journal-entries/{entryId}/reverse",
            new { reason = "ajuste", competenceDate = competence })).EnsureSuccessStatusCode();

    /// <summary>Posts a self-contained company books: capital in, revenue, payroll and energy expense.</summary>
    private static async Task<string> SeedBooksAsync(HttpClient client) =>
        (await SeedBooksWithAccountsAsync(client)).companyId;

    private static async Task<(string companyId, Dictionary<string, string> acc)> SeedBooksWithAccountsAsync(HttpClient client)
    {
        var companyId = await SeedCompanyAsync(client);
        var acc = await SeedChartAsync(client, companyId);
        // AELBRA analytical codes (Asset / Equity / Revenue / Expense / Liability).
        await PostEntryAsync(client, companyId, "2026-01-02", "1.2.05.05.020", "2.6.05.05.020", acc, 100_000m); // capital
        await PostEntryAsync(client, companyId, "2026-01-10", "1.2.05.15.015", "3.2.05.10.020", acc, 50_000m);  // revenue
        await PostEntryAsync(client, companyId, "2026-01-20", "4.2.05.05.020", "1.2.05.15.015", acc, 30_000m);  // payroll
        await PostEntryAsync(client, companyId, "2026-01-25", "4.2.15.05.020", "2.2.05.15.080", acc, 8_000m);   // energy
        return (companyId, acc);
    }

    private static JsonElement Row(JsonElement tb, string code) =>
        tb.GetProperty("rows").EnumerateArray().First(r => r.GetProperty("accountCode").GetString() == code);

    [Fact]
    public async Task TrialBalance_Debit_Equals_Credit()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedBooksAsync(client);

        var tb = await ReadJson(await client.GetAsync($"/api/accounting/trial-balance?companyId={companyId}"));
        Assert.True(tb.GetProperty("isBalanced").GetBoolean());
        Assert.Equal(tb.GetProperty("totalDebit").GetDecimal(), tb.GetProperty("totalCredit").GetDecimal());
        Assert.True(tb.GetProperty("totalDebit").GetDecimal() > 0);
    }

    [Fact]
    public async Task IncomeStatement_Builds_Structured_Cascade()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedBooksAsync(client);

        var dre = await ReadJson(await client.GetAsync(
            $"/api/accounting/income-statement?companyId={companyId}&from=2026-01-01&to=2026-12-31"));

        // End-to-end: the seeded chart carries the DRE line (classifier → column → loader → calculator).
        // Revenue 3.2.05.10.020 → gross revenue; payroll/energy 4.2.* → operating expenses.
        Assert.Equal(50_000m, dre.GetProperty("grossRevenue").GetDecimal());
        Assert.Equal(50_000m, dre.GetProperty("netRevenue").GetDecimal());
        Assert.Equal(50_000m, dre.GetProperty("grossProfit").GetDecimal());
        Assert.Equal(-38_000m, dre.GetProperty("operatingExpenses").GetDecimal());
        Assert.Equal(12_000m, dre.GetProperty("operatingResult").GetDecimal());
        Assert.Equal(12_000m, dre.GetProperty("netIncome").GetDecimal());
    }

    [Fact]
    public async Task BalanceSheet_Ativo_Equals_Passivo_Plus_PL()
    {
        var factory = fixture.Api;
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

    [Fact] // I1: from/to produce opening / period-debit / period-credit / closing per account
    public async Task TrialBalance_Period_Columns()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var (companyId, _) = await SeedBooksWithAccountsAsync(client);

        // Bank account 1.2.05.15.015: +50k (Jan 10) before the window, −30k (Jan 20) inside it.
        var tb = await ReadJson(await client.GetAsync(
            $"/api/accounting/trial-balance?companyId={companyId}&from=2026-01-15&to=2026-01-31"));
        var bank = Row(tb, "1.2.05.15.015");
        Assert.Equal(50_000m, bank.GetProperty("openingBalance").GetDecimal());
        Assert.Equal(0m, bank.GetProperty("totalDebit").GetDecimal());
        Assert.Equal(30_000m, bank.GetProperty("totalCredit").GetDecimal());
        Assert.Equal(20_000m, bank.GetProperty("balance").GetDecimal()); // closing = 50k − 30k
        Assert.True(tb.GetProperty("isBalanced").GetBoolean());
    }

    [Fact] // I2: no range → opening 0 and balance == accumulated (backward compat with the legacy view)
    public async Task TrialBalance_No_Range_Is_All_Time()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var (companyId, _) = await SeedBooksWithAccountsAsync(client);

        var tb = await ReadJson(await client.GetAsync($"/api/accounting/trial-balance?companyId={companyId}"));
        Assert.All(tb.GetProperty("rows").EnumerateArray(), r => Assert.Equal(0m, r.GetProperty("openingBalance").GetDecimal()));
        var bank = Row(tb, "1.2.05.15.015");
        Assert.Equal(20_000m, bank.GetProperty("balance").GetDecimal());
    }

    [Fact] // I3 (criterion b): closing balance == the account's Balanço balance at the same date
    public async Task TrialBalance_Closing_Equals_BalanceSheet()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var (companyId, _) = await SeedBooksWithAccountsAsync(client);

        var tb = await ReadJson(await client.GetAsync(
            $"/api/accounting/trial-balance?companyId={companyId}&to=2026-12-31"));
        var tbBalance = Row(tb, "1.2.05.05.020").GetProperty("balance").GetDecimal();

        var bp = await ReadJson(await client.GetAsync($"/api/accounting/balance-sheet?companyId={companyId}&asOf=2026-12-31"));
        var bpBalance = bp.GetProperty("assets").EnumerateArray()
            .First(a => a.GetProperty("accountCode").GetString() == "1.2.05.05.020")
            .GetProperty("balance").GetDecimal();

        Assert.Equal(100_000m, tbBalance);
        Assert.Equal(bpBalance, tbBalance);
    }

    [Fact] // I4 (criterion a): a closed period reprinted after a later reversal is unchanged
    public async Task TrialBalance_Closed_Period_Is_Immutable_After_Later_Reversal()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAsync(client);
        var acc = await SeedChartAsync(client, companyId);
        var entryId = await PostEntryAsync(client, companyId, "2026-01-10", "4.2.15.05.020", "2.2.05.15.080", acc, 500m);

        var janUrl = $"/api/accounting/trial-balance?companyId={companyId}&from=2026-01-01&to=2026-01-31";
        var before = await (await client.GetAsync(janUrl)).Content.ReadAsStringAsync();

        await ReverseEntryAsync(client, entryId, "2026-02-15");

        var after = await (await client.GetAsync(janUrl)).Content.ReadAsStringAsync();
        Assert.Equal(before, after); // January is untouched by the February reversal

        // Through February the reversal has netted the expense account to zero.
        var feb = await ReadJson(await client.GetAsync(
            $"/api/accounting/trial-balance?companyId={companyId}&to=2026-02-28"));
        Assert.Equal(0m, Row(feb, "4.2.15.05.020").GetProperty("balance").GetDecimal());
    }

    [Fact] // I5: a same-period reversal nets the closing balance to 0 while movement columns show gross churn
    public async Task TrialBalance_Same_Period_Reversal_Nets_But_Shows_Gross_Movement()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAsync(client);
        var acc = await SeedChartAsync(client, companyId);
        var entryId = await PostEntryAsync(client, companyId, "2026-01-10", "4.2.15.05.020", "2.2.05.15.080", acc, 500m);
        await ReverseEntryAsync(client, entryId, "2026-01-20");

        var tb = await ReadJson(await client.GetAsync(
            $"/api/accounting/trial-balance?companyId={companyId}&from=2026-01-01&to=2026-01-31"));
        var energy = Row(tb, "4.2.15.05.020");
        Assert.Equal(500m, energy.GetProperty("totalDebit").GetDecimal());  // original
        Assert.Equal(500m, energy.GetProperty("totalCredit").GetDecimal()); // mirror
        Assert.Equal(0m, energy.GetProperty("balance").GetDecimal());       // nets out (−500 without the loader fix)
        Assert.True(tb.GetProperty("isBalanced").GetBoolean());
    }

    [Fact] // Shared loader fix: the DRE nets a reversal by competence (each stays in its own period)
    public async Task IncomeStatement_Nets_Reversal_By_Competence()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAsync(client);
        var acc = await SeedChartAsync(client, companyId);
        // Expense (energy) against payables in January.
        var entryId = await PostEntryAsync(client, companyId, "2026-01-10", "4.2.15.05.020", "2.2.05.15.080", acc, 500m);
        await ReverseEntryAsync(client, entryId, "2026-02-15");

        var jan = await ReadJson(await client.GetAsync(
            $"/api/accounting/income-statement?companyId={companyId}&from=2026-01-01&to=2026-01-31"));
        // January keeps the original expense (4.2.15 → Despesas Operacionais; contribution = credit − debit = −500).
        Assert.Equal(-500m, jan.GetProperty("operatingExpenses").GetDecimal());
        Assert.Equal(-500m, jan.GetProperty("netIncome").GetDecimal());

        var full = await ReadJson(await client.GetAsync(
            $"/api/accounting/income-statement?companyId={companyId}&from=2026-01-01&to=2026-02-28"));
        // The February reversal nets the expense out across both months.
        Assert.Equal(0m, full.GetProperty("operatingExpenses").GetDecimal());
        Assert.Equal(0m, full.GetProperty("netIncome").GetDecimal());
    }

    [Fact] // Shared loader fix: the Balanço nets a reversal (as-of after the reversal → zero for the account)
    public async Task BalanceSheet_Nets_Reversal()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAsync(client);
        var acc = await SeedChartAsync(client, companyId);
        // Capital in (asset vs equity), then reversed the next month.
        var entryId = await PostEntryAsync(client, companyId, "2026-01-02", "1.2.05.05.020", "2.6.05.05.020", acc, 100_000m);
        await ReverseEntryAsync(client, entryId, "2026-02-10");

        var bp = await ReadJson(await client.GetAsync($"/api/accounting/balance-sheet?companyId={companyId}&asOf=2026-02-28"));
        Assert.True(bp.GetProperty("isBalanced").GetBoolean());
        var asset = bp.GetProperty("assets").EnumerateArray()
            .FirstOrDefault(a => a.GetProperty("accountCode").GetString() == "1.2.05.05.020");
        // Account either absent or zero after the reversal nets out.
        if (asset.ValueKind != JsonValueKind.Undefined)
            Assert.Equal(0m, asset.GetProperty("balance").GetDecimal());
    }

    [Fact] // I7: the cost-center filter reaches the query (a real CC with no movement → no rows; reuses #6-B)
    public async Task TrialBalance_CostCenter_Filter_Is_Applied()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var (companyId, _) = await SeedBooksWithAccountsAsync(client);

        var ccRes = await client.PostAsJsonAsync("/api/cost-centers", new { companyId, code = "CC1", name = "Centro 1" });
        ccRes.EnsureSuccessStatusCode();
        var costCenterId = (await ReadJson(ccRes)).GetProperty("id").GetString()!;

        // Books carry no cost center → filtering by a real CC returns no rows; unfiltered returns rows.
        var filtered = await ReadJson(await client.GetAsync(
            $"/api/accounting/trial-balance?companyId={companyId}&costCenterId={costCenterId}"));
        Assert.Empty(filtered.GetProperty("rows").EnumerateArray());

        var unfiltered = await ReadJson(await client.GetAsync($"/api/accounting/trial-balance?companyId={companyId}"));
        Assert.NotEmpty(unfiltered.GetProperty("rows").EnumerateArray());
    }

    [Fact] // Export: BOM CSV with the balancete header, all accounts and the totals row
    public async Task TrialBalance_Export_Returns_Bom_Csv()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var (companyId, _) = await SeedBooksWithAccountsAsync(client);

        var res = await client.GetAsync($"/api/accounting/trial-balance/export?companyId={companyId}");
        res.EnsureSuccessStatusCode();
        Assert.Equal("text/csv", res.Content.Headers.ContentType?.MediaType);

        var bytes = await res.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF); // UTF-8 BOM

        var csv = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains("codigo;codigo_reduzido;conta;saldo_anterior;debitos;creditos;saldo_final", csv);
        Assert.Contains("1.2.05.15.015", csv); // an account with movement
        Assert.Contains("Totais", csv);
    }

    [Fact] // Year-end closing: zeroes the result into Apuração, transfers the net to Lucros Acumulados,
           // keeps the result on the DRE (closing entries excluded) and closes the exercise; idempotent.
    public async Task YearEndClosing_Clears_Result_And_Transfers_To_Retained_Earnings()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedBooksAsync(client); // 2026: revenue 50k − (payroll 30k + energy 8k) = 12k profit

        var closeRes = await client.PostAsJsonAsync("/api/accounting/year-end-closing", new { companyId, fiscalYear = 2026 });
        closeRes.EnsureSuccessStatusCode();
        var closing = await ReadJson(closeRes);
        Assert.False(closing.GetProperty("alreadyClosed").GetBoolean());
        Assert.Equal(12_000m, closing.GetProperty("netIncome").GetDecimal());

        // The DRE still shows the operating result — the Closing entries are excluded.
        var dre = await ReadJson(await client.GetAsync(
            $"/api/accounting/income-statement?companyId={companyId}&from=2026-01-01&to=2026-12-31"));
        Assert.Equal(12_000m, dre.GetProperty("netIncome").GetDecimal());

        // Trial balance (includes the Closing entries): result accounts + Apuração zeroed, result in Lucros Acumulados.
        var tb = await ReadJson(await client.GetAsync(
            $"/api/accounting/trial-balance?companyId={companyId}&from=2026-01-01&to=2026-12-31"));
        Assert.True(tb.GetProperty("isBalanced").GetBoolean());
        Assert.Equal(0m, Row(tb, "3.2.05.10.020").GetProperty("balance").GetDecimal());    // receita zerada
        Assert.Equal(0m, Row(tb, "4.2.05.05.020").GetProperty("balance").GetDecimal());    // despesa zerada
        Assert.Equal(0m, Row(tb, "2.6.20.05.100").GetProperty("balance").GetDecimal());    // Apuração transitória zerada
        Assert.Equal(12_000m, Row(tb, "2.6.20.05.020").GetProperty("balance").GetDecimal()); // Lucros Acumulados

        // The exercise's December period is closed.
        var periods = await ReadJson(await client.GetAsync($"/api/accounting/periods?companyId={companyId}&fiscalYear=2026"));
        var december = periods.EnumerateArray().First(p => p.GetProperty("month").GetInt32() == 12);
        Assert.Equal("Closed", december.GetProperty("status").GetString());

        // Idempotent: re-running reports it was already closed and posts nothing new.
        var closeAgain = await client.PostAsJsonAsync("/api/accounting/year-end-closing", new { companyId, fiscalYear = 2026 });
        closeAgain.EnsureSuccessStatusCode();
        Assert.True((await ReadJson(closeAgain)).GetProperty("alreadyClosed").GetBoolean());
    }

    [Fact]
    public async Task Consolidated_Aggregates_Across_Companies()
    {
        var factory = fixture.Api;
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
