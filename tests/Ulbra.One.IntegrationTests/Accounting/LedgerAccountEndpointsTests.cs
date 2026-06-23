using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ulbra.One.IntegrationTests.Accounting;

[Collection("Postgres")]
public class LedgerAccountEndpointsTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private static async Task<string> SeedCompanyAsync(HttpClient client)
    {
        var cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        var res = await client.PostAsJsonAsync("/api/companies", new { legalName = "ULBRA Contábil Teste", cnpj });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("id").GetString()!;
    }

    private static object NewSynthetic(string companyId, string code, string name, string group) => new
    {
        companyId, code, name, accountGroup = group, kind = "Synthetic",
    };

    private static object NewAnalytical(string companyId, string code, string name, string group, string parentId) => new
    {
        companyId, code, name, accountGroup = group, kind = "Analytical", parentId,
    };

    [Fact]
    public async Task Anonymous_Returns_401()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = factory.CreateClient();
        var res = await client.GetAsync("/api/ledger-accounts");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Create_Get_List_Update_Flow()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAsync(client);

        var rootRes = await client.PostAsJsonAsync("/api/ledger-accounts",
            NewSynthetic(companyId, "5", "Despesas", "Expense"));
        Assert.Equal(HttpStatusCode.Created, rootRes.StatusCode);
        var root = await ReadJson(rootRes);
        var rootId = root.GetProperty("id").GetString()!;
        // Expense → default normal balance Debit.
        Assert.Equal("Debit", root.GetProperty("normalBalance").GetString());

        var childRes = await client.PostAsJsonAsync("/api/ledger-accounts",
            NewAnalytical(companyId, "5.01.01", "Energia elétrica", "Expense", rootId));
        Assert.Equal(HttpStatusCode.Created, childRes.StatusCode);
        var child = await ReadJson(childRes);
        var childId = child.GetProperty("id").GetString()!;
        Assert.Equal(rootId, child.GetProperty("parentId").GetString());

        // Get by id
        var getRes = await client.GetAsync($"/api/ledger-accounts/{childId}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        Assert.Equal("Energia elétrica", (await ReadJson(getRes)).GetProperty("name").GetString());

        // List by company — ordered by code
        var list = await ReadJson(await client.GetAsync($"/api/ledger-accounts?companyId={companyId}"));
        var codes = list.EnumerateArray().Select(a => a.GetProperty("code").GetString()!).ToList();
        Assert.Contains("5", codes);
        Assert.Contains("5.01.01", codes);
        Assert.True(codes.IndexOf("5") < codes.IndexOf("5.01.01"));

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
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAsync(client);

        var first = await client.PostAsJsonAsync("/api/ledger-accounts", NewSynthetic(companyId, "1", "Ativo", "Asset"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/ledger-accounts", NewSynthetic(companyId, "1", "Ativo dup", "Asset"));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("LedgerAccount.DuplicateCode", (await ReadJson(second)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Get_NonExistent_Returns_404()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var res = await client.GetAsync($"/api/ledger-accounts/ledgacct_{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("LedgerAccount.NotFound", (await ReadJson(res)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Seed_Is_Idempotent_And_Creates_Standard_Chart()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAsync(client);

        var seedRes = await client.PostAsJsonAsync("/api/ledger-accounts/seed", new { companyId });
        Assert.Equal(HttpStatusCode.Created, seedRes.StatusCode);
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

        // Second call is a no-op returning the same chart (200, same count).
        var second = await client.PostAsJsonAsync("/api/ledger-accounts/seed", new { companyId });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(count, (await ReadJson(second)).GetArrayLength());
    }
}
