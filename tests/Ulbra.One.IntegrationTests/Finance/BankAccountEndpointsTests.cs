using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ulbra.One.IntegrationTests.Finance;

[Collection("Postgres")]
public class BankAccountEndpointsTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    /// <summary>Seeds a company via the public API and returns its prefixed id.</summary>
    private static async Task<string> SeedCompanyAsync(HttpClient client)
    {
        var cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        var res = await client.PostAsJsonAsync("/api/companies", new { legalName = "ULBRA Teste", cnpj });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("id").GetString()!;
    }

    private static object NewAccount(string companyId, string accountNumber = "00012345", string accountDigit = "6",
        string alias = "Conta Movimento", string bankCode = "033", string agency = "1234") => new
    {
        companyId,
        bankCode,
        bankName = "Banco Santander",
        agency,
        accountNumber,
        accountDigit,
        accountType = "Checking",
        alias,
        openingBalance = 10_000m,
    };

    // ---------------------------------------------------------------------------
    // Auth
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Anonymous_Returns_401()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = factory.CreateClient();
        var res = await client.GetAsync("/api/bank-accounts");
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
        var companyId = await SeedCompanyAsync(client);

        // 1. Create
        var createRes = await client.PostAsJsonAsync("/api/bank-accounts", NewAccount(companyId, alias: "Conta Movimento"));
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = await ReadJson(createRes);
        var id = created.GetProperty("id").GetString();
        Assert.NotNull(id);
        Assert.Equal("Conta Movimento", created.GetProperty("alias").GetString());
        Assert.Equal(companyId, created.GetProperty("companyId").GetString());
        Assert.True(created.GetProperty("active").GetBoolean());

        // 2. List (active only) filtered by company — present
        var list = await ReadJson(await client.GetAsync($"/api/bank-accounts?companyId={companyId}"));
        Assert.Equal(JsonValueKind.Array, list.ValueKind);
        Assert.Contains(list.EnumerateArray(), a => a.GetProperty("id").GetString() == id);

        // 3. Update — rename and deactivate
        var updateRes = await client.PutAsJsonAsync($"/api/bank-accounts/{id}", new
        {
            bankCode = "033",
            bankName = "Banco Santander",
            agency = "1234",
            accountNumber = "00012345",
            accountDigit = "6",
            accountType = "Savings",
            alias = "Conta Poupanca",
            openingBalance = 5_000m,
            active = false,
        });
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);
        var updated = await ReadJson(updateRes);
        Assert.Equal("Conta Poupanca", updated.GetProperty("alias").GetString());
        Assert.Equal("Savings", updated.GetProperty("accountType").GetString());
        Assert.False(updated.GetProperty("active").GetBoolean());

        // 4. List default (active-only) — absent
        var afterDeactivate = await ReadJson(await client.GetAsync($"/api/bank-accounts?companyId={companyId}"));
        Assert.DoesNotContain(afterDeactivate.EnumerateArray(), a => a.GetProperty("id").GetString() == id);

        // 5. List includeInactive=true — present
        var withInactive = await ReadJson(await client.GetAsync($"/api/bank-accounts?companyId={companyId}&includeInactive=true"));
        Assert.Contains(withInactive.EnumerateArray(), a => a.GetProperty("id").GetString() == id);

        // 6. Delete unused — 204
        var deleteRes = await client.DeleteAsync($"/api/bank-accounts/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteRes.StatusCode);

        // 7. Deleted no longer appears even with includeInactive=true
        var afterDelete = await ReadJson(await client.GetAsync($"/api/bank-accounts?companyId={companyId}&includeInactive=true"));
        Assert.DoesNotContain(afterDelete.EnumerateArray(), a => a.GetProperty("id").GetString() == id);
    }

    // ---------------------------------------------------------------------------
    // Duplicate
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Create_Duplicate_Returns_400_BankAccountDuplicate()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAsync(client);

        var first = await client.PostAsJsonAsync("/api/bank-accounts", NewAccount(companyId));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/bank-accounts", NewAccount(companyId, alias: "Outro Apelido"));
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Equal("BankAccount.Duplicate", (await ReadJson(second)).GetProperty("title").GetString());
    }

    // ---------------------------------------------------------------------------
    // Missing required field
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Create_Blank_Alias_Returns_400_NameRequired()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAsync(client);

        var res = await client.PostAsJsonAsync("/api/bank-accounts", NewAccount(companyId, alias: "   "));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("BankAccount.NameRequired", (await ReadJson(res)).GetProperty("title").GetString());
    }

    // ---------------------------------------------------------------------------
    // Update non-existent
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Update_NonExistent_Returns_404()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        var res = await client.PutAsJsonAsync($"/api/bank-accounts/bank_account_{Guid.NewGuid():N}", new
        {
            bankCode = "033",
            bankName = "Banco Santander",
            agency = "1234",
            accountNumber = "00012345",
            accountDigit = "6",
            accountType = "Checking",
            alias = "X",
            openingBalance = (decimal?)null,
            active = true,
        });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("BankAccount.NotFound", (await ReadJson(res)).GetProperty("title").GetString());
    }

    // ---------------------------------------------------------------------------
    // Delete non-existent
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Delete_NonExistent_Returns_404()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        var res = await client.DeleteAsync($"/api/bank-accounts/bank_account_{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("BankAccount.NotFound", (await ReadJson(res)).GetProperty("title").GetString());
    }

    // ---------------------------------------------------------------------------
    // Default paying account (one-per-company)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Create_With_IsDefaultForPayments_Sets_Default_And_GET_Reflects_It()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAsync(client);

        var createRes = await client.PostAsJsonAsync("/api/bank-accounts", new
        {
            companyId,
            bankCode = "033",
            bankName = "Banco Santander",
            agency = "1234",
            accountNumber = "00012345",
            accountDigit = "6",
            accountType = "Checking",
            alias = "Conta Default",
            openingBalance = 5_000m,
            isDefaultForPayments = true,
            defaultPaymentMethod = "Pix",
        });

        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = await ReadJson(createRes);
        Assert.True(created.GetProperty("isDefaultForPayments").GetBoolean());
        Assert.Equal("Pix", created.GetProperty("defaultPaymentMethod").GetString());
    }

    [Fact]
    public async Task Create_Second_Default_Clears_First()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAsync(client);

        // First default account.
        var first = await client.PostAsJsonAsync("/api/bank-accounts", new
        {
            companyId,
            bankCode = "001",
            bankName = "BB",
            agency = "0001",
            accountNumber = "111111",
            accountDigit = "1",
            accountType = "Checking",
            alias = "Conta A",
            isDefaultForPayments = true,
            defaultPaymentMethod = "Ted",
        });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstId = (await ReadJson(first)).GetProperty("id").GetString()!;

        // Second default account (different account number to avoid duplicate).
        var second = await client.PostAsJsonAsync("/api/bank-accounts", new
        {
            companyId,
            bankCode = "341",
            bankName = "Itaú",
            agency = "2222",
            accountNumber = "222222",
            accountDigit = "2",
            accountType = "Checking",
            alias = "Conta B",
            isDefaultForPayments = true,
            defaultPaymentMethod = "Pix",
        });
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var secondBody = await ReadJson(second);
        Assert.True(secondBody.GetProperty("isDefaultForPayments").GetBoolean());

        // First account is no longer the default.
        var listRes = await client.GetAsync($"/api/bank-accounts?companyId={companyId}");
        var list = await ReadJson(listRes);
        var firstEntry = list.EnumerateArray().FirstOrDefault(a => a.GetProperty("id").GetString() == firstId);
        Assert.Equal(JsonValueKind.Object, firstEntry.ValueKind);
        Assert.False(firstEntry.GetProperty("isDefaultForPayments").GetBoolean());
    }
}
