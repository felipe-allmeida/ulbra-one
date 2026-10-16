using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ulbra.One.IntegrationTests;

namespace Ulbra.One.IntegrationTests.Finance;

[Collection("Postgres")]
public class BankAccountEndpointsTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    /// <summary>Seeds a company via the public API and returns its prefixed id.</summary>
    private static async Task<string> SeedCompanyAsync(HttpClient client)
    {
        var cnpj = TestDocuments.RandomCnpj();
        var res = await client.PostAsJsonAsync("/api/companies", new { legalName = "ULBRA Teste", cnpj });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("id").GetString()!;
    }

    private static object NewAccount(string companyId, string? ledgerAccountId = null, string accountNumber = "00012345", string accountDigit = "6",
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
        ledgerAccountId,
    };

    // ---------------------------------------------------------------------------
    // Auth
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Anonymous_Returns_401()
    {
        var factory = fixture.Api;
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
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAsync(client);
        var ledgerId = await FinanceSeed.CreateAccountingAccountAsync(client);

        // 1. Create
        var createRes = await client.PostAsJsonAsync("/api/bank-accounts", NewAccount(companyId, ledgerId, alias: "Conta Movimento"));
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
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAsync(client);
        var ledgerId = await FinanceSeed.CreateAccountingAccountAsync(client);

        var first = await client.PostAsJsonAsync("/api/bank-accounts", NewAccount(companyId, ledgerId));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/bank-accounts", NewAccount(companyId, ledgerId, alias: "Outro Apelido"));
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Equal("bank_account.duplicate", (await ReadJson(second)).GetProperty("error_code").GetString());
    }

    // ---------------------------------------------------------------------------
    // Missing required field
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Create_Blank_Alias_Returns_400_NameRequired()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAsync(client);

        var res = await client.PostAsJsonAsync("/api/bank-accounts", NewAccount(companyId, alias: "   "));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("bank_account.name_required", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    // ---------------------------------------------------------------------------
    // Update non-existent
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Update_NonExistent_Returns_404()
    {
        var factory = fixture.Api;
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
        Assert.Equal("bank_account.not_found", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    // ---------------------------------------------------------------------------
    // Delete non-existent
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Delete_NonExistent_Returns_404()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();

        var res = await client.DeleteAsync($"/api/bank-accounts/bank_account_{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("bank_account.not_found", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    // ---------------------------------------------------------------------------
    // Default paying account (one-per-company)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Create_With_IsDefaultForPayments_Sets_Default_And_GET_Reflects_It()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAsync(client);
        var ledgerId = await FinanceSeed.CreateAccountingAccountAsync(client);

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
            ledgerAccountId = ledgerId,
        });

        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = await ReadJson(createRes);
        Assert.True(created.GetProperty("isDefaultForPayments").GetBoolean());
        // AEL-933: the account carries no payment method (the forma comes from the installment).
        Assert.False(created.TryGetProperty("defaultPaymentMethod", out _));
    }

    [Fact]
    public async Task Create_Second_Default_Clears_First()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAsync(client);
        var ledgerId = await FinanceSeed.CreateAccountingAccountAsync(client);

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
            ledgerAccountId = ledgerId,
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
            ledgerAccountId = ledgerId,
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

    [Fact]
    public async Task Create_Default_Without_Method_Succeeds()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = await SeedCompanyAsync(client);
        var ledgerId = await FinanceSeed.CreateAccountingAccountAsync(client);

        // AEL-933: a default paying account no longer requires (or carries) a payment method — the forma
        // comes from each installment. Marking it default without a method just works.
        var res = await client.PostAsJsonAsync("/api/bank-accounts", new
        {
            companyId,
            bankCode = "033",
            bankName = "Banco Santander",
            agency = "1234",
            accountNumber = "00012345",
            accountDigit = "6",
            accountType = "Checking",
            alias = "Conta Default Sem Metodo",
            isDefaultForPayments = true,
            ledgerAccountId = ledgerId,
        });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.True((await ReadJson(res)).GetProperty("isDefaultForPayments").GetBoolean());
    }
}
