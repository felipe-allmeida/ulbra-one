using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ulbra.One.IntegrationTests.Finance;

[Collection("Postgres")]
public class ExpenseCategoryEndpointsTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    // ---------------------------------------------------------------------------
    // Auth
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Anonymous_Returns_401()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = factory.CreateClient();
        var res = await client.GetAsync("/api/expense-categories");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    // ---------------------------------------------------------------------------
    // Full CRUD flow
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Create_List_Update_Deactivate_Delete_Flow()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        // 1. Create a category
        var createRes = await client.PostAsJsonAsync("/api/expense-categories", new { name = "Utilities" });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = await ReadJson(createRes);
        var id = created.GetProperty("id").GetString();
        Assert.NotNull(id);
        Assert.Equal("Utilities", created.GetProperty("name").GetString());
        Assert.True(created.GetProperty("active").GetBoolean());

        // 2. List — default (active only) — new category is present
        var listRes = await client.GetAsync("/api/expense-categories");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var list = await ReadJson(listRes);
        Assert.Equal(JsonValueKind.Array, list.ValueKind);
        Assert.Contains(list.EnumerateArray(), c => c.GetProperty("id").GetString() == id);

        // 3. Update — rename and deactivate
        var updateRes = await client.PutAsJsonAsync($"/api/expense-categories/{id}", new { name = "Utilities Renamed", active = false });
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);
        var updated = await ReadJson(updateRes);
        Assert.Equal("Utilities Renamed", updated.GetProperty("name").GetString());
        Assert.False(updated.GetProperty("active").GetBoolean());

        // 4. List (default, active-only) — deactivated category is absent
        var listAfterDeactivate = await ReadJson(await client.GetAsync("/api/expense-categories"));
        Assert.DoesNotContain(listAfterDeactivate.EnumerateArray(), c => c.GetProperty("id").GetString() == id);

        // 5. List with ?includeInactive=true — deactivated category is present
        var listWithInactive = await ReadJson(await client.GetAsync("/api/expense-categories?includeInactive=true"));
        Assert.Contains(listWithInactive.EnumerateArray(), c => c.GetProperty("id").GetString() == id);

        // 6. Re-activate it
        var reactivateRes = await client.PutAsJsonAsync($"/api/expense-categories/{id}", new { name = "Utilities Renamed", active = true });
        Assert.Equal(HttpStatusCode.OK, reactivateRes.StatusCode);
        Assert.True((await ReadJson(reactivateRes)).GetProperty("active").GetBoolean());

        // 7. Delete unused category — 204
        var deleteRes = await client.DeleteAsync($"/api/expense-categories/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteRes.StatusCode);

        // 8. Deleted category no longer appears even with includeInactive=true
        var listAfterDelete = await ReadJson(await client.GetAsync("/api/expense-categories?includeInactive=true"));
        Assert.DoesNotContain(listAfterDelete.EnumerateArray(), c => c.GetProperty("id").GetString() == id);
    }

    // ---------------------------------------------------------------------------
    // Duplicate name
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Create_Duplicate_Name_Returns_400_ExpenseCategoryDuplicate()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        var first = await client.PostAsJsonAsync("/api/expense-categories", new { name = "Marketing" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/expense-categories", new { name = "Marketing" });
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Equal("ExpenseCategory.Duplicate", (await ReadJson(second)).GetProperty("title").GetString());
    }

    // ---------------------------------------------------------------------------
    // Delete non-existent
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Delete_NonExistent_Returns_404()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        var res = await client.DeleteAsync($"/api/expense-categories/expense_category_{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("ExpenseCategory.NotFound", (await ReadJson(res)).GetProperty("title").GetString());
    }

    // ---------------------------------------------------------------------------
    // Update non-existent
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Update_NonExistent_Returns_404()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        var res = await client.PutAsJsonAsync($"/api/expense-categories/expense_category_{Guid.NewGuid():N}", new { name = "X", active = true });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("ExpenseCategory.NotFound", (await ReadJson(res)).GetProperty("title").GetString());
    }

    // ---------------------------------------------------------------------------
    // Update duplicate name
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Update_Rename_To_Existing_Name_Returns_400_ExpenseCategoryDuplicate()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        var resA = await client.PostAsJsonAsync("/api/expense-categories", new { name = "Rename Source A" });
        Assert.Equal(HttpStatusCode.Created, resA.StatusCode);

        var resB = await client.PostAsJsonAsync("/api/expense-categories", new { name = "Rename Target B" });
        Assert.Equal(HttpStatusCode.Created, resB.StatusCode);
        var idB = (await ReadJson(resB)).GetProperty("id").GetString();
        Assert.NotNull(idB);

        var putRes = await client.PutAsJsonAsync($"/api/expense-categories/{idB}", new { name = "Rename Source A", active = true });
        Assert.Equal(HttpStatusCode.BadRequest, putRes.StatusCode);
        Assert.Equal("ExpenseCategory.Duplicate", (await ReadJson(putRes)).GetProperty("title").GetString());
    }
}
