using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ulbra.One.IntegrationTests.Accounting;

[Collection("Postgres")]
public class JournalEntryEndpointsTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private static async Task<string> SeedCompanyAsync(HttpClient client)
    {
        var cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        var res = await client.PostAsJsonAsync("/api/companies", new { legalName = "ULBRA Lançamentos Teste", cnpj });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("id").GetString()!;
    }

    /// <summary>Seeds the company's standard chart and returns a code → accountId map for analytical accounts.</summary>
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

    private static async Task<JsonElement> CreateDraftAsync(HttpClient client, string companyId, string competence, params object[] lines)
    {
        var res = await client.PostAsJsonAsync("/api/journal-entries", new
        {
            companyId,
            competenceDate = competence,
            description = "Lançamento de teste",
            lines,
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return await ReadJson(res);
    }

    [Fact]
    public async Task Anonymous_Returns_401()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = factory.CreateClient();
        var res = await client.GetAsync("/api/journal-entries");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task CreateDraft_Edit_Post_Reverse_Flow()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAsync(client);
        var acc = await SeedChartAsync(client, companyId);

        // Draft: debit energy, credit bank.
        var draft = await CreateDraftAsync(client, companyId, "2026-01-15",
            Line(acc["4.02.02.01.05"], "Debit", 1_000m),
            Line(acc["1.01.01.02.01"], "Credit", 1_000m));
        var id = draft.GetProperty("id").GetString()!;
        Assert.Equal("Draft", draft.GetProperty("status").GetString());
        Assert.True(draft.GetProperty("number").ValueKind == JsonValueKind.Null);

        // Edit draft: change amounts.
        var editRes = await client.PutAsJsonAsync($"/api/journal-entries/{id}", new
        {
            competenceDate = "2026-01-20",
            description = "Energia elétrica - janeiro",
            lines = new[]
            {
                Line(acc["4.02.02.01.05"], "Debit", 1_500m),
                Line(acc["1.01.01.02.01"], "Credit", 1_500m),
            },
        });
        Assert.Equal(HttpStatusCode.OK, editRes.StatusCode);
        var edited = await ReadJson(editRes);
        Assert.Equal(1_500m, edited.GetProperty("totalDebit").GetDecimal());

        // Post: number assigned, status Posted.
        var postRes = await client.PostAsync($"/api/journal-entries/{id}/post", null);
        Assert.Equal(HttpStatusCode.OK, postRes.StatusCode);
        var posted = await ReadJson(postRes);
        Assert.Equal("Posted", posted.GetProperty("status").GetString());
        Assert.Equal(1, posted.GetProperty("number").GetInt32());

        // Immutable after post: edit → 409.
        var editAfterPost = await client.PutAsJsonAsync($"/api/journal-entries/{id}", new
        {
            competenceDate = "2026-01-20",
            description = "tentativa",
            lines = new[]
            {
                Line(acc["4.02.02.01.05"], "Debit", 999m),
                Line(acc["1.01.01.02.01"], "Credit", 999m),
            },
        });
        Assert.Equal(HttpStatusCode.Conflict, editAfterPost.StatusCode);

        // Reverse: produces a new balanced posted entry with mirrored sides.
        var reverseRes = await client.PostAsJsonAsync($"/api/journal-entries/{id}/reverse", new { reason = "erro de digitação" });
        Assert.Equal(HttpStatusCode.Created, reverseRes.StatusCode);
        var reversal = await ReadJson(reverseRes);
        Assert.Equal("Posted", reversal.GetProperty("status").GetString());
        Assert.Equal(2, reversal.GetProperty("number").GetInt32());
        Assert.Equal(id, reversal.GetProperty("reversalOfEntryId").GetString());
        // Mirrored: the account that was debited is now credited.
        var debitLine = reversal.GetProperty("lines").EnumerateArray()
            .First(l => l.GetProperty("ledgerAccountId").GetString() == acc["4.02.02.01.05"]);
        Assert.Equal("Credit", debitLine.GetProperty("side").GetString());
        Assert.Equal(reversal.GetProperty("totalDebit").GetDecimal(), reversal.GetProperty("totalCredit").GetDecimal());

        // Original is now Reversed.
        var original = await ReadJson(await client.GetAsync($"/api/journal-entries/{id}"));
        Assert.Equal("Reversed", original.GetProperty("status").GetString());

        // List filtered by company + status.
        var draftList = await ReadJson(await client.GetAsync($"/api/journal-entries?companyId={companyId}&status=Posted"));
        Assert.All(draftList.EnumerateArray(), e => Assert.Equal("Posted", e.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task Post_Unbalanced_Returns_400()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAsync(client);
        var acc = await SeedChartAsync(client, companyId);

        var draft = await CreateDraftAsync(client, companyId, "2026-02-01",
            Line(acc["4.02.02.01.05"], "Debit", 1_000m),
            Line(acc["1.01.01.02.01"], "Credit", 900m));
        var id = draft.GetProperty("id").GetString()!;

        var postRes = await client.PostAsync($"/api/journal-entries/{id}/post", null);
        Assert.Equal(HttpStatusCode.BadRequest, postRes.StatusCode);
        Assert.Equal("JournalEntry.Unbalanced", (await ReadJson(postRes)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Create_With_Single_Line_Returns_400()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAsync(client);
        var acc = await SeedChartAsync(client, companyId);

        var res = await client.PostAsJsonAsync("/api/journal-entries", new
        {
            companyId,
            competenceDate = "2026-02-01",
            description = "uma linha só",
            lines = new[] { Line(acc["4.02.02.01.05"], "Debit", 100m) },
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("JournalEntry.TooFewLines", (await ReadJson(res)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Create_With_NonPositive_Amount_Returns_400_With_NonPositiveAmount_Code()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAsync(client);
        var acc = await SeedChartAsync(client, companyId);

        var res = await client.PostAsJsonAsync("/api/journal-entries", new
        {
            companyId,
            competenceDate = "2026-02-01",
            description = "valor não positivo",
            lines = new[]
            {
                Line(acc["4.02.02.01.05"], "Debit", 0m),
                Line(acc["1.01.01.02.01"], "Credit", 100m),
            },
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("JournalEntry.NonPositiveAmount", (await ReadJson(res)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Create_With_Synthetic_Account_Line_Returns_400()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAsync(client);
        var acc = await SeedChartAsync(client, companyId);

        // A synthetic account is not postable.
        var syntheticList = await ReadJson(await client.GetAsync("/api/ledger-accounts?kind=Synthetic"));
        var syntheticId = syntheticList.EnumerateArray().First().GetProperty("id").GetString()!;
        var res = await client.PostAsJsonAsync("/api/journal-entries", new
        {
            companyId,
            competenceDate = "2026-02-01",
            description = "conta sintética",
            lines = new[]
            {
                Line(syntheticId, "Debit", 100m),
                Line(acc["1.01.01.02.01"], "Credit", 100m),
            },
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("JournalEntry.NonPostableAccount", (await ReadJson(res)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Post_Assigns_Sequential_PerCompany_Numbers()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAsync(client);
        var acc = await SeedChartAsync(client, companyId);

        var numbers = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            var draft = await CreateDraftAsync(client, companyId, "2026-03-01",
                Line(acc["4.02.02.01.05"], "Debit", 10m),
                Line(acc["1.01.01.02.01"], "Credit", 10m));
            var id = draft.GetProperty("id").GetString()!;
            var posted = await ReadJson(await client.PostAsync($"/api/journal-entries/{id}/post", null));
            numbers.Add(posted.GetProperty("number").GetInt32());
        }
        Assert.Equal(new[] { 1, 2, 3 }, numbers);
    }
}
