using System.Text;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ulbra.One.IntegrationTests.Accounting;

/// <summary>Livro Razão (#2) endpoint + CSV export over real HTTP + DB: acceptance criteria a/b/c, pagination,
/// account-root filter and the BOM CSV export.</summary>
[Collection("Postgres")]
public class GeneralLedgerEndpointTests(PostgresFixture fixture)
{
    // AELBRA analytical codes (same set as AccountingReportEndpointsTests).
    private const string CapitalBankCode = "1.2.05.05.020"; // asset, only the 100k capital entry
    private const string BankCode = "1.2.05.15.015";        // asset: revenue 50k in, payroll 30k out
    private const string EquityCode = "2.6.05.05.020";
    private const string RevenueCode = "3.2.05.10.020";
    private const string PayrollCode = "4.2.05.05.020";
    private const string EnergyCode = "4.2.15.05.020";
    private const string PayablesCode = "2.2.05.15.080";

    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private static async Task<string> SeedCompanyAsync(HttpClient client)
    {
        var cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        var res = await client.PostAsJsonAsync("/api/companies", new { legalName = "ULBRA Razão Teste", cnpj });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("id").GetString()!;
    }

    private static async Task<Dictionary<string, string>> SeedChartAsync(HttpClient client, string companyId)
    {
        var seedRes = await client.PostAsJsonAsync("/api/ledger-accounts/seed", new { companyId });
        seedRes.EnsureSuccessStatusCode();
        return (await ReadJson(seedRes)).EnumerateArray()
            .ToDictionary(a => a.GetProperty("code").GetString()!, a => a.GetProperty("id").GetString()!);
    }

    private static async Task<string> PostEntryAsync(HttpClient client, string companyId, string competence,
        string debitCode, string creditCode, Dictionary<string, string> acc, decimal amount)
    {
        var createRes = await client.PostAsJsonAsync("/api/journal-entries", new
        {
            companyId,
            competenceDate = competence,
            description = $"{debitCode} -> {creditCode}",
            lines = new[]
            {
                new { ledgerAccountId = acc[debitCode], side = "Debit", amount },
                new { ledgerAccountId = acc[creditCode], side = "Credit", amount },
            },
        });
        createRes.EnsureSuccessStatusCode();
        var id = (await ReadJson(createRes)).GetProperty("id").GetString()!;
        (await client.PostAsync($"/api/journal-entries/{id}/post", null)).EnsureSuccessStatusCode();
        return id;
    }

    private static async Task<(string companyId, Dictionary<string, string> acc)> SeedBooksAsync(HttpClient client)
    {
        var companyId = await SeedCompanyAsync(client);
        var acc = await SeedChartAsync(client, companyId);
        await PostEntryAsync(client, companyId, "2026-01-02", CapitalBankCode, EquityCode, acc, 100_000m);
        await PostEntryAsync(client, companyId, "2026-01-10", BankCode, RevenueCode, acc, 50_000m);
        await PostEntryAsync(client, companyId, "2026-01-20", PayrollCode, BankCode, acc, 30_000m);
        await PostEntryAsync(client, companyId, "2026-01-25", EnergyCode, PayablesCode, acc, 8_000m);
        return (companyId, acc);
    }

    [Fact] // (a) sums + running balance of an account
    public async Task Ledger_sums_and_running_balance_for_an_account()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var (companyId, acc) = await SeedBooksAsync(client);

        var page = await ReadJson(await client.GetAsync(
            $"/api/accounting/ledger?companyId={companyId}&ledgerAccountId={acc[BankCode]}&from=2026-01-01&to=2026-12-31"));
        var account = page.GetProperty("items")[0];
        var movements = account.GetProperty("movements");

        Assert.Equal(BankCode, account.GetProperty("accountCode").GetString());
        Assert.Equal(2, movements.GetArrayLength());
        Assert.Equal(50_000m, account.GetProperty("totalDebit").GetDecimal());
        Assert.Equal(30_000m, account.GetProperty("totalCredit").GetDecimal());
        Assert.Equal(20_000m, account.GetProperty("closingBalance").GetDecimal()); // debit-normal: 50k - 30k
        Assert.Equal(50_000m, movements[0].GetProperty("accumulatedBalance").GetDecimal());
        Assert.Equal(20_000m, movements[1].GetProperty("accumulatedBalance").GetDecimal());
    }

    [Fact] // (b) closing balance == balancete balance at the same date
    public async Task Ledger_closing_equals_balance_sheet_balance()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var (companyId, acc) = await SeedBooksAsync(client);

        var ledger = await ReadJson(await client.GetAsync(
            $"/api/accounting/ledger?companyId={companyId}&ledgerAccountId={acc[CapitalBankCode]}&to=2026-12-31"));
        var closing = ledger.GetProperty("items")[0].GetProperty("closingBalance").GetDecimal();

        var bp = await ReadJson(await client.GetAsync(
            $"/api/accounting/balance-sheet?companyId={companyId}&asOf=2026-12-31"));
        var bpBalance = bp.GetProperty("assets").EnumerateArray()
            .First(a => a.GetProperty("accountCode").GetString() == CapitalBankCode)
            .GetProperty("balance").GetDecimal();

        Assert.Equal(100_000m, closing);
        Assert.Equal(bpBalance, closing);
    }

    [Fact] // (c) closed-period read is idempotent; reversal creates a movement on a new date
    public async Task Closed_period_read_is_idempotent_and_reversal_moves_to_new_date()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAsync(client);
        var acc = await SeedChartAsync(client, companyId);
        var entryId = await PostEntryAsync(client, companyId, "2026-03-10", EnergyCode, PayablesCode, acc, 500m);

        (await client.PostAsJsonAsync("/api/accounting/periods/close",
            new { companyId, fiscalYear = 2026, month = 3 })).EnsureSuccessStatusCode();

        // Reads bypass the closed-period guard and are idempotent.
        var url = $"/api/accounting/ledger?companyId={companyId}&ledgerAccountId={acc[EnergyCode]}&from=2026-03-01&to=2026-03-31";
        var first = await (await client.GetAsync(url)).Content.ReadAsStringAsync();
        var second = await (await client.GetAsync(url)).Content.ReadAsStringAsync();
        Assert.Equal(first, second);

        // Reverse the March entry into open April → the mirror lands on the new date.
        var rev = await client.PostAsJsonAsync($"/api/journal-entries/{entryId}/reverse",
            new { reason = "ajuste", competenceDate = "2026-04-15" });
        rev.EnsureSuccessStatusCode();

        var full = await ReadJson(await client.GetAsync(
            $"/api/accounting/ledger?companyId={companyId}&ledgerAccountId={acc[EnergyCode]}&from=2026-01-01&to=2026-04-30"));
        var movements = full.GetProperty("items")[0].GetProperty("movements").EnumerateArray().ToList();
        Assert.Contains(movements, m => m.GetProperty("competenceDate").GetString() == "2026-04-15");
    }

    [Fact]
    public async Task Paginates_by_account_and_filters_by_root()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var (companyId, _) = await SeedBooksAsync(client);

        // 7 distinct accounts touched → pageSize 3 gives 3 items, total 7, 3 pages.
        var p1 = await ReadJson(await client.GetAsync($"/api/accounting/ledger?companyId={companyId}&pageSize=3"));
        Assert.Equal(3, p1.GetProperty("items").GetArrayLength());
        Assert.Equal(7, p1.GetProperty("total").GetInt32());
        Assert.Equal(3, p1.GetProperty("totalPages").GetInt32());

        // root=2 → only accounts whose code starts with "2." (Passivo/PL).
        var root2 = await ReadJson(await client.GetAsync($"/api/accounting/ledger?companyId={companyId}&root=2"));
        var codes = root2.GetProperty("items").EnumerateArray()
            .Select(a => a.GetProperty("accountCode").GetString()!).ToList();
        Assert.NotEmpty(codes);
        Assert.All(codes, c => Assert.StartsWith("2.", c));
    }

    [Fact]
    public async Task Export_returns_bom_csv_with_all_accounts()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var (companyId, _) = await SeedBooksAsync(client);

        var res = await client.GetAsync($"/api/accounting/ledger/export?companyId={companyId}");
        res.EnsureSuccessStatusCode();
        Assert.Equal("text/csv", res.Content.Headers.ContentType?.MediaType);

        var bytes = await res.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF); // UTF-8 BOM

        var csv = Encoding.UTF8.GetString(bytes);
        Assert.Contains(
            "conta;codigo_resumido;nome_conta;data;historico;documento;centro_custo;debito;credito;saldo_acumulado",
            csv);
        Assert.Contains(EnergyCode, csv);       // all filtered accounts, not just one page
        Assert.Contains(CapitalBankCode, csv);
    }
}
