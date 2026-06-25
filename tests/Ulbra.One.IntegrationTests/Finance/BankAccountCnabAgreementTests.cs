using System.Net.Http.Json;
using System.Text.Json;

namespace Ulbra.One.IntegrationTests.Finance;

/// <summary>Regression: the CNAB agreement code (convênio) entered in the bank-account form must
/// survive create AND update through the public endpoints — not be silently dropped at the API
/// boundary. Earlier tests seeded BankAccount via the entity ctor, so the endpoint gap (the request
/// DTO lacked the field; the ctor default filled "") was never exercised — which is why generating a
/// remittance failed with "missing agreement code" even though the user had filled it in.</summary>
[Collection("Postgres")]
public class BankAccountCnabAgreementTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private static async Task<string> CreateCompanyAsync(HttpClient client)
    {
        var cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        var res = await client.PostAsJsonAsync("/api/companies", new { legalName = "ULBRA Teste", cnpj });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("id").GetString()!;
    }

    private static async Task<string?> FetchAgreementCodeAsync(HttpClient client, string companyId, string accountId)
    {
        var list = await client.GetAsync($"/api/bank-accounts?companyId={companyId}");
        list.EnsureSuccessStatusCode();
        var items = await ReadJson(list); // ListBankAccounts returns a bare array, not a paged envelope
        foreach (var item in items.EnumerateArray())
            if (item.GetProperty("id").GetString() == accountId)
                return item.GetProperty("cnabAgreementCode").GetString();
        return null;
    }

    [Fact]
    public async Task Create_persists_cnab_agreement_code()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var companyId = await CreateCompanyAsync(client);

        var res = await client.PostAsJsonAsync("/api/bank-accounts", new
        {
            companyId,
            bankCode = "033", bankName = "Banco Santander", agency = "1234",
            accountNumber = "00012345", accountDigit = "6", accountType = "Checking",
            alias = "Conta pagadora", openingBalance = 10_000m,
            cnabAgreementCode = "987654",
        });
        res.EnsureSuccessStatusCode();
        var created = await ReadJson(res);

        // Response must echo the persisted value...
        Assert.Equal("987654", created.GetProperty("cnabAgreementCode").GetString());
        // ...and a fresh read from the DB (list endpoint) must too.
        var id = created.GetProperty("id").GetString()!;
        Assert.Equal("987654", await FetchAgreementCodeAsync(client, companyId, id));
    }

    [Fact]
    public async Task Update_persists_cnab_agreement_code()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var companyId = await CreateCompanyAsync(client);

        var createRes = await client.PostAsJsonAsync("/api/bank-accounts", new
        {
            companyId,
            bankCode = "033", bankName = "Banco Santander", agency = "1234",
            accountNumber = "00099999", accountDigit = "6", accountType = "Checking",
            alias = "Conta", openingBalance = 0m, cnabAgreementCode = "111",
        });
        createRes.EnsureSuccessStatusCode();
        var id = (await ReadJson(createRes)).GetProperty("id").GetString()!;

        var put = await client.PutAsJsonAsync($"/api/bank-accounts/{id}", new
        {
            bankCode = "033", bankName = "Banco Santander", agency = "1234",
            accountNumber = "00099999", accountDigit = "6", accountType = "Checking",
            alias = "Conta", openingBalance = 0m, active = true,
            cnabAgreementCode = "222",
        });
        put.EnsureSuccessStatusCode();

        Assert.Equal("222", (await ReadJson(put)).GetProperty("cnabAgreementCode").GetString());
        Assert.Equal("222", await FetchAgreementCodeAsync(client, companyId, id));
    }
}
