using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Finance.Infrastructure.Database;

namespace Ulbra.One.IntegrationTests.Finance;

[Collection("Postgres")]
public class ConciliationEndpointsTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private sealed record Seed(string CompanyId, string CostCenterId, string VendorId);

    private static async Task<Seed> SeedAsync(HttpClient client)
    {
        var cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        var companyRes = await client.PostAsJsonAsync("/api/companies", new { legalName = "ULBRA Teste", cnpj });
        companyRes.EnsureSuccessStatusCode();
        var companyId = (await ReadJson(companyRes)).GetProperty("id").GetString()!;

        var ccRes = await client.PostAsJsonAsync("/api/cost-centers",
            new { companyId, code = "CC1", name = "Marketing", annualBudget = 1_000_000m, autoApprovalLimit = 0m, unitIds = Array.Empty<string>() });
        ccRes.EnsureSuccessStatusCode();
        var costCenterId = (await ReadJson(ccRes)).GetProperty("id").GetString()!;

        var vendorRes = await client.PostAsJsonAsync("/api/vendors", new
        {
            companyId,
            personType = 1,
            name = "Fornecedor Sul Ltda",
            tradeName = "Fornecedor Sul",
            document = "11.222.333/0001-81",
            email = "contato@fs.com",
            phone = "5133334444",
            address = (object?)null,
            paymentTerms = "30 dias",
            bankInfo = (string?)null,
        });
        vendorRes.EnsureSuccessStatusCode();
        var vendorId = (await ReadJson(vendorRes)).GetProperty("id").GetString()!;

        return new Seed(companyId, costCenterId, vendorId);
    }

    private static async Task<string> CreateBankAccountAsync(HttpClient client, string companyId)
    {
        var res = await client.PostAsJsonAsync("/api/bank-accounts", new
        {
            companyId,
            bankCode = "033",
            bankName = "Banco Santander",
            agency = "1234",
            accountNumber = "00012345",
            accountDigit = "6",
            accountType = "Checking",
            alias = "Conta principal",
            openingBalance = 10_000m,
        });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("id").GetString()!;
    }

    private static async Task CreateApprovedPayableAsync(HttpClient client, Seed seed, decimal amount, string dueDate)
    {
        var createRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Para pagar",
            installments = new[] { new { dueDate, amount } },
        });
        createRes.EnsureSuccessStatusCode();
        var payableId = (await ReadJson(createRes)).GetProperty("id").GetString()!;
        var approveRes = await client.PostAsJsonAsync($"/api/payables/{payableId}/approve", new { });
        approveRes.EnsureSuccessStatusCode();
    }

    private const string OfxContent = """
        OFXHEADER:100
        DATA:OFXSGML
        VERSION:102
        SECURITY:NONE
        ENCODING:USASCII

        <OFX>
        <SIGNONMSGSRSV1><SONRS><STATUS><CODE>0<SEVERITY>INFO</STATUS><DTSERVER>20260731<LANGUAGE>POR</SONRS></SIGNONMSGSRSV1>
        <BANKMSGSRSV1><STMTTRNRS><TRNUID>0<STATUS><CODE>0<SEVERITY>INFO</STATUS><STMTRS>
        <CURDEF>BRL
        <BANKACCTFROM><BANKID>033<ACCTID>12345<ACCTTYPE>CHECKING</BANKACCTFROM>
        <BANKTRANLIST>
        <DTSTART>20260701
        <DTEND>20260731
        <STMTTRN><TRNTYPE>DEBIT<DTPOSTED>20260710<TRNAMT>-1000.00<FITID>TXN-1<MEMO>PAG FORNEC CEEE</STMTTRN>
        <STMTTRN><TRNTYPE>CREDIT<DTPOSTED>20260712<TRNAMT>2500.00<FITID>TXN-2<MEMO>DEPOSITO</STMTTRN>
        </BANKTRANLIST>
        <LEDGERBAL><BALAMT>1500.00<DTASOF>20260731</LEDGERBAL>
        </STMTRS></STMTTRNRS></BANKMSGSRSV1>
        </OFX>
        """;

    private static MultipartFormDataContent OfxFile()
    {
        var content = new MultipartFormDataContent();
        var fileBytes = new ByteArrayContent(Encoding.ASCII.GetBytes(OfxContent));
        fileBytes.Headers.ContentType = new MediaTypeHeaderValue("application/x-ofx");
        content.Add(fileBytes, "file", "extrato.ofx");
        return content;
    }

    [Fact]
    public async Task ImportStatement_Parses_Dedups_And_Suggests_HighConfidence_Debit()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        await CreateApprovedPayableAsync(client, seed, 1000.00m, "2026-07-12");

        // First import → both transactions are new.
        using (var content = OfxFile())
        {
            var res = await client.PostAsync($"/api/statement-imports?bankAccountId={bankId}", content);
            Assert.Equal(HttpStatusCode.Created, res.StatusCode);
            var body = await ReadJson(res);
            Assert.Equal(2, body.GetProperty("transactionCount").GetInt32());
            Assert.Equal(2, body.GetProperty("newCount").GetInt32());
            Assert.Equal(0, body.GetProperty("duplicateCount").GetInt32());
        }

        // Re-import the SAME file → idempotent: nothing new, both duplicates.
        using (var content = OfxFile())
        {
            var res = await client.PostAsync($"/api/statement-imports?bankAccountId={bankId}", content);
            Assert.Equal(HttpStatusCode.Created, res.StatusCode);
            var body = await ReadJson(res);
            Assert.Equal(2, body.GetProperty("transactionCount").GetInt32());
            Assert.Equal(0, body.GetProperty("newCount").GetInt32());
            Assert.Equal(2, body.GetProperty("duplicateCount").GetInt32());
        }

        // The -1000 debit on 2026-07-10 should be Suggested against the 1000 installment due 2026-07-12.
        var bankAccountGuid = Ulbra.One.Common.Application.Ids.BankAccountId.Parse(bankId, null).Value;
        using var scope = factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<FinanceContext>();
        var txns = ctx.BankTransactions.Where(t => t.BankAccountId == bankAccountGuid).ToList();

        Assert.Equal(2, txns.Count);
        var debit = Assert.Single(txns, t => t.Direction == TransactionDirection.Debit);
        Assert.Equal(MatchStatus.Suggested, debit.MatchStatus);
        Assert.NotNull(debit.SuggestedInstallmentId);

        var credit = Assert.Single(txns, t => t.Direction == TransactionDirection.Credit);
        Assert.Equal(MatchStatus.Unmatched, credit.MatchStatus);
    }

    [Fact]
    public async Task ListStatementImports_FiltersByBankAccount_ReturnsImports()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);

        // Import once so there is at least one record.
        using var content = OfxFile();
        var importRes = await client.PostAsync($"/api/statement-imports?bankAccountId={bankId}", content);
        Assert.Equal(HttpStatusCode.Created, importRes.StatusCode);

        // GET /statement-imports?bankAccountId=…
        var listRes = await client.GetAsync($"/api/statement-imports?bankAccountId={bankId}");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var body = await ReadJson(listRes);
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.True(body.GetArrayLength() >= 1, "Expected at least one statement import.");
        Assert.All(body.EnumerateArray(), item =>
            Assert.Equal(bankId, item.GetProperty("bankAccountId").GetString()));
    }

    [Fact]
    public async Task ListBankTransactions_FiltersByMatchStatus_ReturnsPaginatedResults()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        await CreateApprovedPayableAsync(client, seed, 1000.00m, "2026-07-12");

        using var content = OfxFile();
        var importRes = await client.PostAsync($"/api/statement-imports?bankAccountId={bankId}", content);
        Assert.Equal(HttpStatusCode.Created, importRes.StatusCode);

        // Suggested debit
        var suggestedRes = await client.GetAsync($"/api/bank-transactions?bankAccountId={bankId}&matchStatus=Suggested");
        Assert.Equal(HttpStatusCode.OK, suggestedRes.StatusCode);
        var suggestedPage = await ReadJson(suggestedRes);
        Assert.Equal(1, suggestedPage.GetProperty("total").GetInt32());
        var suggestedItems = suggestedPage.GetProperty("items");
        Assert.Equal(1, suggestedItems.GetArrayLength());

        // Unmatched credit
        var unmatchedRes = await client.GetAsync($"/api/bank-transactions?bankAccountId={bankId}&matchStatus=Unmatched");
        Assert.Equal(HttpStatusCode.OK, unmatchedRes.StatusCode);
        var unmatchedPage = await ReadJson(unmatchedRes);
        Assert.Equal(1, unmatchedPage.GetProperty("total").GetInt32());
        var unmatchedItems = unmatchedPage.GetProperty("items");
        Assert.Equal(1, unmatchedItems.GetArrayLength());
    }
}
