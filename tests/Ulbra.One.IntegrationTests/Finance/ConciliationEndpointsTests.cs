using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Persistence;

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

    private static async Task<string> CreateApprovedPayableAsync(ApiFactory factory, HttpClient client, Seed seed, decimal amount, string dueDate)
    {
        var createRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Para pagar",
            installments = new[] { new { dueDate, amount } },
            type = "Product", // determined product — required before a payable can schedule
        });
        createRes.EnsureSuccessStatusCode();
        var payableId = (await ReadJson(createRes)).GetProperty("id").GetString()!;
        var approveRes = await client.PostAsJsonAsync($"/api/payables/{payableId}/approve", new { });
        approveRes.EnsureSuccessStatusCode();
        await FinanceSeed.CompleteAccountingAsync(factory, client, payableId); // → Scheduled
        return payableId;
    }

    private static async Task ImportAsync(HttpClient client, string bankId)
    {
        using var content = OfxFile();
        var res = await client.PostAsync($"/api/statement-imports?bankAccountId={bankId}", content);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    }

    private static async Task<JsonElement> SingleTransactionAsync(HttpClient client, string bankId, string matchStatus)
    {
        var res = await client.GetAsync($"/api/bank-transactions?bankAccountId={bankId}&matchStatus={matchStatus}");
        res.EnsureSuccessStatusCode();
        var page = await ReadJson(res);
        var items = page.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        return items[0];
    }

    private static async Task<JsonElement> GetInstallmentAsync(HttpClient client, string payableId)
    {
        var res = await client.GetAsync($"/api/payables/{payableId}");
        res.EnsureSuccessStatusCode();
        var body = await ReadJson(res);
        return body.GetProperty("installments")[0];
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
        await CreateApprovedPayableAsync(factory, client, seed, 1000.00m, "2026-07-12");

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
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
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
        await CreateApprovedPayableAsync(factory, client, seed, 1000.00m, "2026-07-12");

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

    [Fact]
    public async Task ConfirmMatch_SettlesInstallment_AndReconcilesTransaction()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        var payableId = await CreateApprovedPayableAsync(factory, client, seed, 1000.00m, "2026-07-12");
        await ImportAsync(client, bankId);

        var suggested = await SingleTransactionAsync(client, bankId, "Suggested");
        var txnId = suggested.GetProperty("id").GetString()!;

        var confirmRes = await client.PostAsJsonAsync($"/api/bank-transactions/{txnId}/confirm", new { });
        Assert.Equal(HttpStatusCode.OK, confirmRes.StatusCode);
        var confirmed = await ReadJson(confirmRes);
        Assert.Equal("Reconciled", confirmed.GetProperty("matchStatus").GetString());

        var installment = await GetInstallmentAsync(client, payableId);
        Assert.Equal("Paid", installment.GetProperty("status").GetString());
        Assert.Equal(1, installment.GetProperty("payments").GetArrayLength());
    }

    [Fact]
    public async Task ConfirmMatch_NonSuggested_Returns409()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        await CreateApprovedPayableAsync(factory, client, seed, 1000.00m, "2026-07-12");
        await ImportAsync(client, bankId);

        var credit = await SingleTransactionAsync(client, bankId, "Unmatched");
        var txnId = credit.GetProperty("id").GetString()!;

        var res = await client.PostAsJsonAsync($"/api/bank-transactions/{txnId}/confirm", new { });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var body = await ReadJson(res);
        Assert.Equal("BankTransaction.NotSuggested", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task MatchManually_EqualRemaining_Reconciles_WrongAmount_Returns400()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        await CreateApprovedPayableAsync(factory, client, seed, 1000.00m, "2026-07-12");
        await ImportAsync(client, bankId);

        // The credit (2500) is Unmatched; match it manually.
        var credit = await SingleTransactionAsync(client, bankId, "Unmatched");
        var creditTxnId = credit.GetProperty("id").GetString()!;

        // Wrong-amount installment: a 1000 installment vs. the 2500 credit → AmountMismatch.
        var wrongPayableId = await CreateApprovedPayableAsync(factory, client, seed, 1000.00m, "2026-07-12");
        var wrongInstallment = await GetInstallmentAsync(client, wrongPayableId);
        var wrongInstallmentId = wrongInstallment.GetProperty("id").GetString()!;

        var wrongRes = await client.PostAsJsonAsync($"/api/bank-transactions/{creditTxnId}/match",
            new { installmentId = wrongInstallmentId });
        Assert.Equal(HttpStatusCode.BadRequest, wrongRes.StatusCode);
        Assert.Equal("Match.AmountMismatch", (await ReadJson(wrongRes)).GetProperty("title").GetString());

        // Correct-amount installment: a 2500 installment that equals the credit → reconciles.
        var rightPayableId = await CreateApprovedPayableAsync(factory, client, seed, 2500.00m, "2026-07-12");
        var rightInstallment = await GetInstallmentAsync(client, rightPayableId);
        var rightInstallmentId = rightInstallment.GetProperty("id").GetString()!;

        var rightRes = await client.PostAsJsonAsync($"/api/bank-transactions/{creditTxnId}/match",
            new { installmentId = rightInstallmentId });
        Assert.Equal(HttpStatusCode.OK, rightRes.StatusCode);
        Assert.Equal("Reconciled", (await ReadJson(rightRes)).GetProperty("matchStatus").GetString());

        var settled = await GetInstallmentAsync(client, rightPayableId);
        Assert.Equal("Paid", settled.GetProperty("status").GetString());
    }

    [Fact]
    public async Task IgnoreTransaction_MarksIgnored()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        await CreateApprovedPayableAsync(factory, client, seed, 1000.00m, "2026-07-12");
        await ImportAsync(client, bankId);

        var credit = await SingleTransactionAsync(client, bankId, "Unmatched");
        var txnId = credit.GetProperty("id").GetString()!;

        var res = await client.PostAsJsonAsync($"/api/bank-transactions/{txnId}/ignore", new { });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("Ignored", (await ReadJson(res)).GetProperty("matchStatus").GetString());
    }

    // ---------------------------------------------------------------------------
    // Conciliation match uses NetAmount (withholding fix, AEL-619)
    // ---------------------------------------------------------------------------

    /// <summary>Generates an OFX SGML snippet with a single debit of <paramref name="debitAmount"/> (positive value → written as negative).</summary>
    private static MultipartFormDataContent OfxFileWithDebit(decimal debitAmount, string fitId = "TXN-WH-1")
    {
        var amountStr = (-debitAmount).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        var ofx = $"""
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
            <STMTTRN><TRNTYPE>DEBIT<DTPOSTED>20260710<TRNAMT>{amountStr}<FITID>{fitId}<MEMO>PAG RETENCAO</STMTTRN>
            </BANKTRANLIST>
            <LEDGERBAL><BALAMT>0.00<DTASOF>20260731</LEDGERBAL>
            </STMTRS></STMTTRNRS></BANKMSGSRSV1>
            </OFX>
            """;
        var content = new MultipartFormDataContent();
        var fileBytes = new ByteArrayContent(Encoding.ASCII.GetBytes(ofx));
        fileBytes.Headers.ContentType = new MediaTypeHeaderValue("application/x-ofx");
        content.Add(fileBytes, "file", "extrato_retencao.ofx");
        return content;
    }

    /// <summary>Seeds a withheld payable (approved/Scheduled) via domain — HTTP CreatePayable does not expose withholdings.</summary>
    private static async Task<(string PayablePrefixedId, Guid InstallmentId)> CreateWithheldPayableAsync(
        ApiFactory factory, Seed seed, decimal grossAmount, decimal withholdingAmount)
    {
        var companyId = Guid.Parse(seed.CompanyId[(seed.CompanyId.LastIndexOf('_') + 1)..]);
        var vendorId = Guid.Parse(seed.VendorId[(seed.VendorId.LastIndexOf('_') + 1)..]);
        var ccId = Guid.Parse(seed.CostCenterId[(seed.CostCenterId.LastIndexOf('_') + 1)..]);

        await using var scope = factory.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        var installment = new Installment(1, new DateOnly(2026, 7, 10), grossAmount);
        var payable = new Payable(
            companyId: companyId,
            origin: PayableOrigin.Manual,
            vendorId: vendorId,
            counterpartyName: "Fornecedor Retencao Conciliacao",
            documentNumber: null,
            issueDate: null,
            description: "Payable with IRRF for conciliation test",
            costCenterId: ccId,
            categoryId: null,
            installments: [installment]);

        payable.SetWithholdings([
            (WithholdingTaxType.Irrf, BaseAmount: grossAmount, Rate: null, Amount: withholdingAmount)
        ]);
        payable.Approve("test-user", clock.GetUtcNow());
        payable.CompleteAccounting("test-user", clock.GetUtcNow(), hasFiscalDocument: true); // → Scheduled

        await uow.AddAsync(payable, CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);

        return ($"payable_{payable.Id:N}", installment.Id);
    }

    [Fact]
    public async Task ConfirmMatch_WithheldPayable_NetAmountDebit_ReconcilesCleanlly()
    {
        // Gross 1200, IRRF 120 → NetAmount = 1080. Bank debits 1080 (the net cash).
        const decimal gross = 1200m;
        const decimal withheld = 120m;
        const decimal net = gross - withheld; // 1080

        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        var (payablePrefixedId, installmentId) = await CreateWithheldPayableAsync(factory, seed, gross, withheld);

        // Import an OFX with a debit of exactly NetAmount.
        using (var content = OfxFileWithDebit(net))
        {
            var importRes = await client.PostAsync($"/api/statement-imports?bankAccountId={bankId}", content);
            Assert.Equal(HttpStatusCode.Created, importRes.StatusCode);
        }

        // The installment's Remaining = NetAmount − 0 = 1080, which matches the debit exactly → Suggested.
        var bankAccountGuid = Ulbra.One.Common.Application.Ids.BankAccountId.Parse(bankId, null).Value;
        using var scope = factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var debit = Assert.Single(ctx.BankTransactions.Where(t => t.BankAccountId == bankAccountGuid));
        Assert.Equal(MatchStatus.Suggested, debit.MatchStatus);
        Assert.Equal(installmentId, debit.SuggestedInstallmentId);

        // Confirm the match → installment must reach Paid and transaction Reconciled.
        var txnPrefixedId = $"bank_txn_{debit.Id:N}";
        var confirmRes = await client.PostAsJsonAsync($"/api/bank-transactions/{txnPrefixedId}/confirm", new { });
        Assert.Equal(HttpStatusCode.OK, confirmRes.StatusCode);
        var confirmed = await ReadJson(confirmRes);
        Assert.Equal("Reconciled", confirmed.GetProperty("matchStatus").GetString());

        var installment = await GetInstallmentAsync(client, payablePrefixedId);
        Assert.Equal("Paid", installment.GetProperty("status").GetString());
        Assert.Equal(1, installment.GetProperty("payments").GetArrayLength());
        Assert.Equal(net, installment.GetProperty("payments")[0].GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task UnmatchTransaction_ReopensInstallment_AndUnmatchesTransaction()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        var payableId = await CreateApprovedPayableAsync(factory, client, seed, 1000.00m, "2026-07-12");
        await ImportAsync(client, bankId);

        var suggested = await SingleTransactionAsync(client, bankId, "Suggested");
        var txnId = suggested.GetProperty("id").GetString()!;

        var confirmRes = await client.PostAsJsonAsync($"/api/bank-transactions/{txnId}/confirm", new { });
        Assert.Equal(HttpStatusCode.OK, confirmRes.StatusCode);

        var paid = await GetInstallmentAsync(client, payableId);
        Assert.Equal("Paid", paid.GetProperty("status").GetString());

        var unmatchRes = await client.PostAsJsonAsync($"/api/bank-transactions/{txnId}/unmatch", new { });
        Assert.Equal(HttpStatusCode.OK, unmatchRes.StatusCode);
        Assert.Equal("Unmatched", (await ReadJson(unmatchRes)).GetProperty("matchStatus").GetString());

        var reopened = await GetInstallmentAsync(client, payableId);
        Assert.Equal("Open", reopened.GetProperty("status").GetString());
    }
}
