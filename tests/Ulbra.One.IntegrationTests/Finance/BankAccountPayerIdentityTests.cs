using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ulbra.One.IntegrationTests;

namespace Ulbra.One.IntegrationTests.Finance;

/// <summary>
/// The CNAB payer identity override (PayerCnpj / PayerLegalName) entered in the bank-account form must
/// survive create AND update through the public endpoints — normalized to digits — and an invalid CNPJ
/// must be rejected at the boundary (400) rather than overflowing the fixed-width CNAB field later. (AEL-910)
/// </summary>
[Collection("Postgres")]
public class BankAccountPayerIdentityTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private static async Task<string> CreateCompanyAsync(HttpClient client)
    {
        var cnpj = TestDocuments.RandomCnpj();
        var res = await client.PostAsJsonAsync("/api/companies", new { legalName = "ULBRA Teste", cnpj });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> FetchAccountAsync(HttpClient client, string companyId, string accountId)
    {
        var list = await client.GetAsync($"/api/bank-accounts?companyId={companyId}");
        list.EnsureSuccessStatusCode();
        var items = await ReadJson(list); // ListBankAccounts returns a bare array
        foreach (var item in items.EnumerateArray())
            if (item.GetProperty("id").GetString() == accountId)
                return item.Clone();
        throw new Xunit.Sdk.XunitException($"Account {accountId} not found for company {companyId}.");
    }

    [Fact]
    public async Task Create_persists_payer_identity_normalized_to_digits()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = await CreateCompanyAsync(client);
        var ledgerAccountId = await FinanceSeed.CreateAccountingAccountAsync(client);

        var res = await client.PostAsJsonAsync("/api/bank-accounts", new
        {
            companyId,
            bankCode = "033", bankName = "Banco Santander", agency = "1234",
            accountNumber = "00012345", accountDigit = "6", accountType = "Checking",
            alias = "Conta pagadora", openingBalance = 10_000m,
            cnabAgreementCode = "987654",
            payerCnpj = "11.222.333/0001-81", // masked input — must be stored digits-only
            payerLegalName = "ULBRA Estabelecimento EAD",
            ledgerAccountId,
        });
        res.EnsureSuccessStatusCode();
        var created = await ReadJson(res);

        // Response echoes the persisted, normalized values...
        Assert.Equal("11222333000181", created.GetProperty("payerCnpj").GetString());
        Assert.Equal("ULBRA Estabelecimento EAD", created.GetProperty("payerLegalName").GetString());

        // ...and a fresh read from the DB (list endpoint) matches.
        var id = created.GetProperty("id").GetString()!;
        var fromDb = await FetchAccountAsync(client, companyId, id);
        Assert.Equal("11222333000181", fromDb.GetProperty("payerCnpj").GetString());
        Assert.Equal("ULBRA Estabelecimento EAD", fromDb.GetProperty("payerLegalName").GetString());
    }

    [Fact]
    public async Task Update_changes_payer_identity()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = await CreateCompanyAsync(client);
        var ledgerAccountId = await FinanceSeed.CreateAccountingAccountAsync(client);

        var createRes = await client.PostAsJsonAsync("/api/bank-accounts", new
        {
            companyId,
            bankCode = "033", bankName = "Banco Santander", agency = "1234",
            accountNumber = "00099999", accountDigit = "6", accountType = "Checking",
            alias = "Conta", openingBalance = 0m, cnabAgreementCode = "111",
            ledgerAccountId,
        });
        createRes.EnsureSuccessStatusCode();
        var id = (await ReadJson(createRes)).GetProperty("id").GetString()!;

        var put = await client.PutAsJsonAsync($"/api/bank-accounts/{id}", new
        {
            bankCode = "033", bankName = "Banco Santander", agency = "1234",
            accountNumber = "00099999", accountDigit = "6", accountType = "Checking",
            alias = "Conta", openingBalance = 0m, active = true,
            cnabAgreementCode = "111",
            payerCnpj = "06990590000123",
            payerLegalName = "Outra Razão Social",
        });
        put.EnsureSuccessStatusCode();

        Assert.Equal("06990590000123", (await ReadJson(put)).GetProperty("payerCnpj").GetString());
        var fromDb = await FetchAccountAsync(client, companyId, id);
        Assert.Equal("06990590000123", fromDb.GetProperty("payerCnpj").GetString());
        Assert.Equal("Outra Razão Social", fromDb.GetProperty("payerLegalName").GetString());
    }

    [Fact]
    public async Task Create_rejects_invalid_payer_cnpj_with_400()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = await CreateCompanyAsync(client);
        var ledgerAccountId = await FinanceSeed.CreateAccountingAccountAsync(client);

        var res = await client.PostAsJsonAsync("/api/bank-accounts", new
        {
            companyId,
            bankCode = "033", bankName = "Banco Santander", agency = "1234",
            accountNumber = "00012345", accountDigit = "6", accountType = "Checking",
            alias = "Conta inválida", openingBalance = 0m,
            payerCnpj = "11.222.333/0001-99", // valid base, wrong check digits → mod-11 fails
            ledgerAccountId,
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
