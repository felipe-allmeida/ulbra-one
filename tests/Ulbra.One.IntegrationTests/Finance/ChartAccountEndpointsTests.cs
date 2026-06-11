using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ulbra.One.IntegrationTests.Finance;

[Collection("Postgres")]
public class ChartAccountEndpointsTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    /// <summary>A code unlikely to collide with seed data or other test runs.</summary>
    private static string UniqueCode(string prefix) => $"{prefix}.{Random.Shared.Next(100_000, 999_999)}";

    private static object NewSynthetic(string chartType, string code, string name) => new
    {
        chartType,
        code,
        name,
        kind = "Synthetic",
    };

    private static object NewAnalytical(string chartType, string code, string name, string parentId, string nature) => new
    {
        chartType,
        code,
        name,
        kind = "Analytical",
        parentId,
        nature,
    };

    // ---------------------------------------------------------------------------
    // Auth
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Anonymous_Returns_401()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = factory.CreateClient();
        var res = await client.GetAsync("/api/chart-accounts");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    // ---------------------------------------------------------------------------
    // Full CRUD flow
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Create_List_Update_Delete_Flow()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        // Unique chart type-scoped codes per test run (the dev seed already owns "4", "76", …).
        var rootCode = UniqueCode("R");
        var childCode = $"{rootCode}.01";

        // 1. Create synthetic root
        var rootRes = await client.PostAsJsonAsync("/api/chart-accounts", NewSynthetic("Accounting", rootCode, "Despesas"));
        Assert.Equal(HttpStatusCode.Created, rootRes.StatusCode);
        var root = await ReadJson(rootRes);
        var rootId = root.GetProperty("id").GetString()!;
        Assert.Equal(rootCode, root.GetProperty("code").GetString());
        Assert.Equal("Synthetic", root.GetProperty("kind").GetString());

        // 2. Create analytical child
        var childRes = await client.PostAsJsonAsync("/api/chart-accounts",
            NewAnalytical("Accounting", childCode, "Energia elétrica", rootId, "Debit"));
        Assert.Equal(HttpStatusCode.Created, childRes.StatusCode);
        var child = await ReadJson(childRes);
        var childId = child.GetProperty("id").GetString()!;
        Assert.Equal(rootId, child.GetProperty("parentId").GetString());
        Assert.Equal("Debit", child.GetProperty("nature").GetString());

        // 3. List by chartType — both present, ordered by code
        var list = await ReadJson(await client.GetAsync("/api/chart-accounts?chartType=Accounting"));
        Assert.Equal(JsonValueKind.Array, list.ValueKind);
        var codes = list.EnumerateArray().Select(a => a.GetProperty("code").GetString()!).ToList();
        Assert.Contains(rootCode, codes);
        Assert.Contains(childCode, codes);
        // childCode = "<rootCode>.01" sorts right after rootCode → root must come before child.
        Assert.True(codes.IndexOf(rootCode) < codes.IndexOf(childCode),
            "chart accounts should be ordered by code");

        // 4. Rename the child
        var updateRes = await client.PutAsJsonAsync($"/api/chart-accounts/{childId}", new
        {
            name = "Energia (renomeada)",
            parentId = rootId,
            nature = "Debit",
            isActive = true,
        });
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);
        Assert.Equal("Energia (renomeada)", (await ReadJson(updateRes)).GetProperty("name").GetString());

        // 5. Delete root with a child → 409 HasChildren
        var delRoot = await client.DeleteAsync($"/api/chart-accounts/{rootId}");
        Assert.Equal(HttpStatusCode.Conflict, delRoot.StatusCode);
        Assert.Equal("ChartAccount.HasChildren", (await ReadJson(delRoot)).GetProperty("title").GetString());

        // 6. Delete childless leaf → 204, then absent from the list
        var delLeaf = await client.DeleteAsync($"/api/chart-accounts/{childId}");
        Assert.Equal(HttpStatusCode.NoContent, delLeaf.StatusCode);

        var afterDelete = await ReadJson(await client.GetAsync("/api/chart-accounts?chartType=Accounting"));
        Assert.DoesNotContain(afterDelete.EnumerateArray(), a => a.GetProperty("id").GetString() == childId);
    }

    // ---------------------------------------------------------------------------
    // Parent of a different chart type → 400 InvalidParent
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Create_Child_With_DifferentChartType_Parent_Returns_400_InvalidParent()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        var rootCode = UniqueCode("P");
        var accountingRoot = await ReadJson(await client.PostAsJsonAsync("/api/chart-accounts",
            NewSynthetic("Accounting", rootCode, "Receitas")));
        var accountingRootId = accountingRoot.GetProperty("id").GetString()!;

        // Financial child pointing at an Accounting parent.
        var res = await client.PostAsJsonAsync("/api/chart-accounts",
            NewAnalytical("Financial", $"{rootCode}.1", "Filho inválido", accountingRootId, "Credit"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("ChartAccount.InvalidParent", (await ReadJson(res)).GetProperty("title").GetString());
    }

    // ---------------------------------------------------------------------------
    // Duplicate (chartType, code) → 409 DuplicateCode
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Create_Duplicate_Code_Returns_409_DuplicateCode()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        var code = UniqueCode("D");
        var first = await client.PostAsJsonAsync("/api/chart-accounts", NewSynthetic("Accounting", code, "Original"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/chart-accounts", NewSynthetic("Accounting", code, "Duplicada"));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("ChartAccount.DuplicateCode", (await ReadJson(second)).GetProperty("title").GetString());
    }

    // ---------------------------------------------------------------------------
    // Delete non-existent
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Delete_NonExistent_Returns_404()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        var res = await client.DeleteAsync($"/api/chart-accounts/chart_account_{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("ChartAccount.NotFound", (await ReadJson(res)).GetProperty("title").GetString());
    }
}
