using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ulbra.One.IntegrationTests.Accounting;

[Collection("Postgres")]
public class LedgerAccountEndpointsTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    // The chart of accounts is global with a unique code index. Use a unique, alpha-prefixed code so tests
    // don't collide with the startup-seeded AELBRA chart (numeric dotted codes) or with each other on the shared DB.
    private static string UniqueCode() => $"T{Random.Shared.Next(1_000_000, 9_999_999)}";

    private static object NewSynthetic(string code, string name, string group) => new
    {
        code, name, accountGroup = group, kind = "Synthetic",
    };

    private static object NewAnalytical(string code, string name, string group, string parentId) => new
    {
        code, name, accountGroup = group, kind = "Analytical", parentId,
    };

    [Fact]
    public async Task Anonymous_Returns_401()
    {
        var factory = fixture.Api;
        var client = factory.CreateClient();
        var res = await client.GetAsync("/api/ledger-accounts");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Create_Get_List_Update_Flow()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();

        var rootCode = UniqueCode();
        var rootRes = await client.PostAsJsonAsync("/api/ledger-accounts",
            NewSynthetic(rootCode, "Despesas", "Expense"));
        Assert.Equal(HttpStatusCode.Created, rootRes.StatusCode);
        var root = await ReadJson(rootRes);
        var rootId = root.GetProperty("id").GetString()!;
        // Expense → default normal balance Debit.
        Assert.Equal("Debit", root.GetProperty("normalBalance").GetString());

        var childCode = $"{rootCode}.01";
        var childRes = await client.PostAsJsonAsync("/api/ledger-accounts",
            NewAnalytical(childCode, "Energia elétrica", "Expense", rootId));
        Assert.Equal(HttpStatusCode.Created, childRes.StatusCode);
        var child = await ReadJson(childRes);
        var childId = child.GetProperty("id").GetString()!;
        Assert.Equal(rootId, child.GetProperty("parentId").GetString());

        // Get by id
        var getRes = await client.GetAsync($"/api/ledger-accounts/{childId}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        Assert.Equal("Energia elétrica", (await ReadJson(getRes)).GetProperty("name").GetString());

        // List (global) — ordered by code; contains the two created accounts in order.
        var list = await ReadJson(await client.GetAsync("/api/ledger-accounts"));
        var codes = list.EnumerateArray().Select(a => a.GetProperty("code").GetString()!).ToList();
        Assert.Contains(rootCode, codes);
        Assert.Contains(childCode, codes);
        Assert.True(codes.IndexOf(rootCode) < codes.IndexOf(childCode));

        // Update (rename + keep parent + active)
        var updateRes = await client.PutAsJsonAsync($"/api/ledger-accounts/{childId}", new
        {
            name = "Energia (renomeada)",
            accountGroup = "Expense",
            kind = "Analytical",
            normalBalance = "Debit",
            parentId = rootId,
            isActive = true,
        });
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);
        Assert.Equal("Energia (renomeada)", (await ReadJson(updateRes)).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Create_Duplicate_Code_Returns_409()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();

        var code = UniqueCode();
        var first = await client.PostAsJsonAsync("/api/ledger-accounts", NewSynthetic(code, "Ativo", "Asset"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/ledger-accounts", NewSynthetic(code, "Ativo dup", "Asset"));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("ledger_account.duplicate_code", (await ReadJson(second)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Get_NonExistent_Returns_404()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var res = await client.GetAsync($"/api/ledger-accounts/ledgacct_{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("ledger_account.not_found", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Seed_Is_Idempotent_And_Chart_Has_The_Five_Groups()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();

        // The global chart is seeded once at startup; seed is idempotent → returns 200 with the existing chart.
        var seedRes = await client.PostAsJsonAsync("/api/ledger-accounts/seed", new { });
        Assert.Equal(HttpStatusCode.OK, seedRes.StatusCode);
        var seeded = await ReadJson(seedRes);
        var count = seeded.GetArrayLength();
        Assert.True(count >= 15, $"expected a populated chart, got {count}");

        // Has the five synthetic group roots.
        var groups = seeded.EnumerateArray()
            .Where(a => a.GetProperty("kind").GetString() == "Synthetic")
            .Select(a => a.GetProperty("accountGroup").GetString())
            .Distinct()
            .ToList();
        Assert.Contains("Asset", groups);
        Assert.Contains("Liability", groups);
        Assert.Contains("Equity", groups);
        Assert.Contains("Revenue", groups);
        Assert.Contains("Expense", groups);

        // Second call is also a no-op returning the same chart (200, same count).
        var second = await client.PostAsJsonAsync("/api/ledger-accounts/seed", new { });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(count, (await ReadJson(second)).GetArrayLength());
    }
}
