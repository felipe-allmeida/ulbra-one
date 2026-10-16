using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ulbra.One.IntegrationTests;

namespace Ulbra.One.IntegrationTests.Accounting;

/// <summary>Livro Diário read-only endpoint (#12) and the manual closed-period guard (#1) over real HTTP + DB.</summary>
[Collection("Postgres")]
public class JournalBookEndpointTests(PostgresFixture fixture)
{
    // AELBRA analytical codes used across these tests.
    private const string ExpenseCode = "4.2.05.05.020";
    private const string PayablesCode = "2.2.05.15.080";

    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private static async Task<string> SeedCompanyAsync(HttpClient client)
    {
        var cnpj = TestDocuments.RandomCnpj();
        var res = await client.PostAsJsonAsync("/api/companies", new { legalName = "ULBRA Livro Diário Teste", cnpj });
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

    private static async Task<HttpResponseMessage> CreateEntryAsync(
        HttpClient client, string companyId, string competence, Dictionary<string, string> acc, decimal amount) =>
        await client.PostAsJsonAsync("/api/journal-entries", new
        {
            companyId,
            competenceDate = competence,
            description = "Despesa",
            lines = new[]
            {
                new { ledgerAccountId = acc[ExpenseCode], side = "Debit", amount },
                new { ledgerAccountId = acc[PayablesCode], side = "Credit", amount },
            },
        });

    private static async Task<string> PostEntryAsync(
        HttpClient client, string companyId, string competence, Dictionary<string, string> acc, decimal amount)
    {
        var createRes = await CreateEntryAsync(client, companyId, competence, acc, amount);
        createRes.EnsureSuccessStatusCode();
        var id = (await ReadJson(createRes)).GetProperty("id").GetString()!;
        (await client.PostAsync($"/api/journal-entries/{id}/post", null)).EnsureSuccessStatusCode();
        return id;
    }

    private static async Task<string> SeedUnitAsync(HttpClient client, string companyId)
    {
        var res = await client.PostAsJsonAsync("/api/units", new { companyId, name = "Campus Diário", acronym = "CD" });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("id").GetString()!;
    }

    // Posts a balanced entry whose expense (debit) leg is tagged with the unit; the balancing credit leg carries no
    // unit — mirroring an automatic payable incurral (only the result line carries the analytical dimension).
    private static async Task PostEntryWithUnitAsync(
        HttpClient client, string companyId, string competence, Dictionary<string, string> acc, decimal amount, string unitId)
    {
        var createRes = await client.PostAsJsonAsync("/api/journal-entries", new
        {
            companyId,
            competenceDate = competence,
            description = "Despesa",
            lines = new object[]
            {
                new { ledgerAccountId = acc[ExpenseCode], side = "Debit", amount, unitId },
                new { ledgerAccountId = acc[PayablesCode], side = "Credit", amount },
            },
        });
        createRes.EnsureSuccessStatusCode();
        var id = (await ReadJson(createRes)).GetProperty("id").GetString()!;
        (await client.PostAsync($"/api/journal-entries/{id}/post", null)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task JournalBook_groups_by_day_excludes_drafts_and_totals_match()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAsync(client);
        var acc = await SeedChartAsync(client, companyId);

        await PostEntryAsync(client, companyId, "2026-02-01", acc, 1_000m);
        await PostEntryAsync(client, companyId, "2026-02-02", acc, 500m);
        // A draft (created but not posted) must NOT appear in the book.
        (await CreateEntryAsync(client, companyId, "2026-02-03", acc, 999m)).EnsureSuccessStatusCode();

        var book = await ReadJson(await client.GetAsync(
            $"/api/accounting/journal-book?companyId={companyId}&from=2026-02-01&to=2026-02-28"));

        var days = book.GetProperty("days");
        Assert.Equal(2, days.GetArrayLength()); // draft day excluded
        Assert.Equal("2026-02-01", days[0].GetProperty("date").GetString());
        Assert.Equal(1_500m, book.GetProperty("totalDebit").GetDecimal());
        Assert.Equal(1_500m, book.GetProperty("totalCredit").GetDecimal());
        // Lines carry the account labels (self-contained book).
        var firstLine = days[0].GetProperty("entries")[0].GetProperty("lines")[0];
        Assert.False(string.IsNullOrEmpty(firstLine.GetProperty("accountCode").GetString()));
    }

    [Fact]
    public async Task JournalBook_filters_by_unit_keeping_whole_entries()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAsync(client);
        var acc = await SeedChartAsync(client, companyId);
        var unitId = await SeedUnitAsync(client, companyId);

        await PostEntryWithUnitAsync(client, companyId, "2026-02-01", acc, 1_000m, unitId); // tagged with the unit
        await PostEntryAsync(client, companyId, "2026-02-02", acc, 500m); // no unit

        var book = await ReadJson(await client.GetAsync(
            $"/api/accounting/journal-book?companyId={companyId}&from=2026-02-01&to=2026-02-28&unitId={unitId}"));

        var days = book.GetProperty("days");
        Assert.Equal(1, days.GetArrayLength()); // only the unit's entry day (the untagged entry is excluded)
        Assert.Equal("2026-02-01", days[0].GetProperty("date").GetString());
        var entries = days[0].GetProperty("entries");
        Assert.Equal(1, entries.GetArrayLength());
        // Entry-level filter: the whole balanced entry is kept — both legs, incl. the untagged credit line.
        Assert.Equal(2, entries[0].GetProperty("lines").GetArrayLength());
        Assert.Equal(1_000m, book.GetProperty("totalDebit").GetDecimal());
        Assert.Equal(1_000m, book.GetProperty("totalCredit").GetDecimal());
    }

    [Fact]
    public async Task Cannot_create_entry_in_closed_period()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAsync(client);
        var acc = await SeedChartAsync(client, companyId);

        (await client.PostAsJsonAsync("/api/accounting/periods/close",
            new { companyId, fiscalYear = 2026, month = 1 })).EnsureSuccessStatusCode();

        var blocked = await CreateEntryAsync(client, companyId, "2026-01-15", acc, 100m);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        // A different, open month still posts.
        var open = await CreateEntryAsync(client, companyId, "2026-02-15", acc, 100m);
        open.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Cannot_reverse_into_a_closed_period_then_reopen_allows_it()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAsync(client);
        var acc = await SeedChartAsync(client, companyId);

        var id = await PostEntryAsync(client, companyId, "2026-03-10", acc, 100m);
        (await client.PostAsJsonAsync("/api/accounting/periods/close",
            new { companyId, fiscalYear = 2026, month = 3 })).EnsureSuccessStatusCode();

        // Reversal defaults to the original competence (March, now closed) → blocked.
        var blocked = await client.PostAsJsonAsync($"/api/journal-entries/{id}/reverse", new { reason = "erro" });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        // Reopen March → the reversal is allowed again.
        (await client.PostAsJsonAsync("/api/accounting/periods/reopen",
            new { companyId, fiscalYear = 2026, month = 3 })).EnsureSuccessStatusCode();
        var ok = await client.PostAsJsonAsync($"/api/journal-entries/{id}/reverse", new { reason = "erro" });
        ok.EnsureSuccessStatusCode();
    }
}
