using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.IntegrationTests;
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
        var cnpj = TestDocuments.RandomCnpj();
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
            document = TestCnpj.New(),
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

    private static async Task<string> CreateBankAccountAsync(HttpClient client, string companyId, string? ofxAccountId = null)
    {
        var ledgerAccountId = await FinanceSeed.CreateAccountingAccountAsync(client);
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
            ledgerAccountId,
            ofxAccountId = ofxAccountId ?? "",
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
        var factory = fixture.Api;
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
        var txns = ctx.Set<BankTransaction>().Where(t => t.BankAccountId == bankAccountGuid).ToList();

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
        var factory = fixture.Api;
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
    public async Task ListBankTransactions_ExplainsAValueDivergence_AndOffersTheNearMiss()
    {
        // The reconciliation screen shows a reason, not a bare "não conciliado". This is the whole chain:
        // pool query → matcher explanation → contract → JSON, against a real database.
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        // The statement debit is 1.000,00 on 10/07. A 1.010,00 título due the same day is juros away from it —
        // close enough to show, too far for the matcher to pair on its own.
        var payableId = await CreateApprovedPayableAsync(factory, client, seed, 1010.00m, "2026-07-10");
        await ImportAsync(client, bankId);

        // The statement's credit line is unmatched too, so narrow to the debit this test is about.
        var res = await client.GetAsync($"/api/bank-transactions?bankAccountId={bankId}&matchStatus=Unmatched&direction=Debit");
        res.EnsureSuccessStatusCode();
        var debit = Assert.Single((await ReadJson(res)).GetProperty("items").EnumerateArray().ToList());
        Assert.Equal("AmountDiffers", debit.GetProperty("mismatch").GetString());

        var candidates = debit.GetProperty("candidates");
        Assert.Equal(1, candidates.GetArrayLength());
        var candidate = candidates[0];
        Assert.Equal(10.00m, candidate.GetProperty("amountDelta").GetDecimal());
        Assert.Equal(0, candidate.GetProperty("dayDelta").GetInt32());
        Assert.Equal(1010.00m, candidate.GetProperty("remaining").GetDecimal());
        Assert.Equal(payableId, candidate.GetProperty("payableId").GetString());
        Assert.Equal("Fornecedor Sul Ltda", candidate.GetProperty("counterpartyName").GetString());
        Assert.Equal(1, candidate.GetProperty("sequence").GetInt32());
        Assert.Equal(1, candidate.GetProperty("installmentCount").GetInt32());

        var installment = await GetInstallmentAsync(client, payableId);
        Assert.Equal(installment.GetProperty("id").GetString(), candidate.GetProperty("installmentId").GetString());
    }

    [Fact]
    public async Task ListBankTransactions_SaysACreditIsUnmatchedByDesign_NotByFailure()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        // Pairs the debit off as Suggested, so the only Unmatched line left is the credit.
        await CreateApprovedPayableAsync(factory, client, seed, 1000.00m, "2026-07-12");
        await ImportAsync(client, bankId);

        var credit = await SingleTransactionAsync(client, bankId, "Unmatched");
        Assert.Equal("Credit", credit.GetProperty("direction").GetString());
        Assert.Equal("CreditNotMatched", credit.GetProperty("mismatch").GetString());
        Assert.Equal(0, credit.GetProperty("candidates").GetArrayLength());
    }

    [Fact]
    public async Task ListBankTransactions_CarriesTheSuggestedInstallment_SoTheRowCanNameTheVendor()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        var payableId = await CreateApprovedPayableAsync(factory, client, seed, 1000.00m, "2026-07-12");
        await ImportAsync(client, bankId);

        var suggested = await SingleTransactionAsync(client, bankId, "Suggested");
        // A suggestion is not a mismatch — but the parcela behind it still travels with the row.
        Assert.Equal(JsonValueKind.Null, suggested.GetProperty("mismatch").ValueKind);
        var candidate = Assert.Single(suggested.GetProperty("candidates").EnumerateArray().ToList());
        Assert.Equal(payableId, candidate.GetProperty("payableId").GetString());
        Assert.Equal(0m, candidate.GetProperty("amountDelta").GetDecimal());
        Assert.Equal(2, candidate.GetProperty("dayDelta").GetInt32());
    }

    [Fact]
    public async Task ListBankTransactions_FiltersByMatchStatus_ReturnsPaginatedResults()
    {
        var factory = fixture.Api;
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
        var factory = fixture.Api;
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
    public async Task ImportStatement_LinksADebitWhoseInstallmentWasAlreadySettled()
    {
        // The CNAB-return case: the baixa happened BEFORE the statement was imported, so the installment is
        // already Paid and never enters the open-installment matcher. Its debit used to sit "não conciliado"
        // forever even though the payment was right there.
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        var payableId = await CreateApprovedPayableAsync(factory, client, seed, 1000.00m, "2026-07-12");

        // Baixa registrada antes da importação, na mesma conta/valor/data do débito do extrato.
        var installmentId = (await GetInstallmentAsync(client, payableId)).GetProperty("id").GetString()!;
        var payRes = await client.PostAsJsonAsync(
            $"/api/payables/{payableId}/installments/{installmentId}/payments",
            new { paymentDate = "2026-07-10", amount = 1000.00m, bankAccountId = bankId, method = "Other" });
        payRes.EnsureSuccessStatusCode();

        await ImportAsync(client, bankId);

        var linked = await SingleTransactionAsync(client, bankId, "Reconciled");
        Assert.True(linked.GetProperty("paymentPreexisting").GetBoolean());
        Assert.False(string.IsNullOrEmpty(linked.GetProperty("matchedPaymentId").GetString()));

        // Nothing was registered by the conciliation: still exactly ONE payment on the installment.
        var installment = await GetInstallmentAsync(client, payableId);
        Assert.Equal("Paid", installment.GetProperty("status").GetString());
        Assert.Equal(1, installment.GetProperty("payments").GetArrayLength());
    }

    [Fact]
    public async Task ReimportingSweepsDebitsLeftUnmatchedByAnEarlierImport()
    {
        // The real production case: the statement was imported BEFORE the baixa existed (or before the linking
        // did), so the debit sat unmatched. Re-importing the same file is deduped by FITID and brings in nothing
        // new — the sweep still has to reach the debit that is already sitting there.
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);

        // 1) Import first, with no payable at all → the -1000,00 debit lands unmatched.
        await ImportAsync(client, bankId);
        // The file carries a debit AND a credit; with no payable yet both are unmatched, so pick the debit.
        var unmatchedRes = await client.GetAsync($"/api/bank-transactions?bankAccountId={bankId}&matchStatus=Unmatched");
        unmatchedRes.EnsureSuccessStatusCode();
        var orphans = (await ReadJson(unmatchedRes)).GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(orphans, o => o.GetProperty("amount").GetDecimal() == -1000.00m);

        // 2) Only now the título exists and is settled on that same account/date/amount.
        var payableId = await CreateApprovedPayableAsync(factory, client, seed, 1000.00m, "2026-07-12");
        var installmentId = (await GetInstallmentAsync(client, payableId)).GetProperty("id").GetString()!;
        var payRes = await client.PostAsJsonAsync(
            $"/api/payables/{payableId}/installments/{installmentId}/payments",
            new { paymentDate = "2026-07-10", amount = 1000.00m, bankAccountId = bankId, method = "Other" });
        payRes.EnsureSuccessStatusCode();

        // 3) Re-import the SAME file: every line is a duplicate, so nothing new is ingested...
        using var content = OfxFile();
        var again = await client.PostAsync($"/api/statement-imports?bankAccountId={bankId}", content);
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        var body = await ReadJson(again);
        Assert.Equal(0, body.GetProperty("newCount").GetInt32());

        // ...and yet the debit that was already sitting there gets linked.
        var linked = await SingleTransactionAsync(client, bankId, "Reconciled");
        Assert.True(linked.GetProperty("paymentPreexisting").GetBoolean());

        var installment = await GetInstallmentAsync(client, payableId);
        Assert.Equal(1, installment.GetProperty("payments").GetArrayLength());
    }

    [Fact]
    public async Task UnmatchingALinkedPayment_UnlinksButNeverReversesIt()
    {
        // The footgun this guards: undoing this kind of link must not reverse a payment the bank really made.
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        var payableId = await CreateApprovedPayableAsync(factory, client, seed, 1000.00m, "2026-07-12");

        var installmentId = (await GetInstallmentAsync(client, payableId)).GetProperty("id").GetString()!;
        var payRes = await client.PostAsJsonAsync(
            $"/api/payables/{payableId}/installments/{installmentId}/payments",
            new { paymentDate = "2026-07-10", amount = 1000.00m, bankAccountId = bankId, method = "Other" });
        payRes.EnsureSuccessStatusCode();

        await ImportAsync(client, bankId);
        var linked = await SingleTransactionAsync(client, bankId, "Reconciled");
        var txnId = linked.GetProperty("id").GetString()!;

        var unmatchRes = await client.PostAsJsonAsync($"/api/bank-transactions/{txnId}/unmatch", new { });
        Assert.Equal(HttpStatusCode.OK, unmatchRes.StatusCode);
        var after = await ReadJson(unmatchRes);
        Assert.Equal("Unmatched", after.GetProperty("matchStatus").GetString());

        // The payment survives untouched — the installment is still settled.
        var installment = await GetInstallmentAsync(client, payableId);
        Assert.Equal("Paid", installment.GetProperty("status").GetString());
        Assert.Equal(1, installment.GetProperty("payments").GetArrayLength());
        Assert.False(installment.GetProperty("payments")[0].GetProperty("isReversed").GetBoolean());
    }

    [Fact]
    public async Task ConfirmMatch_NonSuggested_Returns409()
    {
        var factory = fixture.Api;
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
        Assert.Equal("bank_transaction.not_suggested", body.GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task MatchManually_EqualRemaining_Reconciles_WrongAmount_Returns400()
    {
        var factory = fixture.Api;
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
        Assert.Equal("match.amount_mismatch", (await ReadJson(wrongRes)).GetProperty("error_code").GetString());

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
        var factory = fixture.Api;
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

        var factory = fixture.Api;
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
        var debit = Assert.Single(ctx.Set<BankTransaction>().Where(t => t.BankAccountId == bankAccountGuid));
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
        var factory = fixture.Api;
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

    // ---------------------------------------------------------------------------
    // AEL-767: real Santander format (pt-BR decimals, CHARSET:1252, bracketed TZ,
    // unclosed leaf tags), account identification (warn), period summary, direction
    // filter, and the duplicate-amount one-to-one matcher.
    // ---------------------------------------------------------------------------

    // Faithful to a Santander site export: no blank line before <OFX>, unclosed leaf tags,
    // comma decimals, a dot-thousands+comma value, bracketed-timezone single-day period.
    private const string SantanderOfxContent = """
        OFXHEADER:100
        DATA:OFXSGML
        VERSION:102
        SECURITY:NONE
        ENCODING:USASCII
        CHARSET:1252
        COMPRESSION:NONE
        OLDFILEUID:NONE
        NEWFILEUID:NONE
        <OFX>
        <SIGNONMSGSRSV1><SONRS><STATUS><CODE>0<SEVERITY>INFO</STATUS><DTSERVER>20260615120151[-3:GMT]<LANGUAGE>ENG<FI><ORG>SANTANDER<FID>SANTANDER</FI></SONRS></SIGNONMSGSRSV1>
        <BANKMSGSRSV1><STMTTRNRS><TRNUID>1<STATUS><CODE>0<SEVERITY>INFO</STATUS><STMTRS>
        <CURDEF>BRL
        <BANKACCTFROM>
            <BANKID>033
            <ACCTID>3578130509766
            <ACCTTYPE>CHECKING
        </BANKACCTFROM>
        <BANKTRANLIST>
            <DTSTART>20260612000000[-3:GMT]
            <DTEND>20260612000000[-3:GMT]
            <STMTTRN>
                <TRNTYPE>DEBIT
                <DTPOSTED>20260612000000[-3:GMT]
                <TRNAMT>-200,00
                <FITID>S1
                <CHECKNUM>493627
                <MEMO>TARIFA PAGFOR COMPROVANTE LOTE
            </STMTTRN>
            <STMTTRN>
                <TRNTYPE>CREDIT
                <DTPOSTED>20260612000000[-3:GMT]
                <TRNAMT>2650.000,00
                <FITID>S2
                <CHECKNUM>18404534
                <MEMO>PIX RECEBIDO
            </STMTTRN>
            <STMTTRN>
                <TRNTYPE>DEBIT
                <DTPOSTED>20260612000000[-3:GMT]
                <TRNAMT>-1975,87
                <FITID>S3
                <CHECKNUM>030612
                <MEMO>PAGFOR PIX OUTRA INST- DIFEREN TIT
            </STMTTRN>
        </BANKTRANLIST>
        <LEDGERBAL><BALAMT>586577,87<DTASOF>20260615120151[-3:GMT]</LEDGERBAL>
        </STMTRS></STMTTRNRS></BANKMSGSRSV1>
        </OFX>
        """;

    // A statement whose account clearly differs from the created one (bank 001, ACCTID 99999999).
    private const string ForeignAccountOfxContent = """
        OFXHEADER:100
        DATA:OFXSGML
        VERSION:102
        SECURITY:NONE
        ENCODING:USASCII

        <OFX>
        <SIGNONMSGSRSV1><SONRS><STATUS><CODE>0<SEVERITY>INFO</STATUS><DTSERVER>20260731<LANGUAGE>POR</SONRS></SIGNONMSGSRSV1>
        <BANKMSGSRSV1><STMTTRNRS><TRNUID>0<STATUS><CODE>0<SEVERITY>INFO</STATUS><STMTRS>
        <CURDEF>BRL
        <BANKACCTFROM><BANKID>001<ACCTID>99999999<ACCTTYPE>CHECKING</BANKACCTFROM>
        <BANKTRANLIST>
        <DTSTART>20260701
        <DTEND>20260731
        <STMTTRN><TRNTYPE>DEBIT<DTPOSTED>20260710<TRNAMT>-50,00<FITID>FA-1<MEMO>TARIFA</STMTTRN>
        </BANKTRANLIST>
        <LEDGERBAL><BALAMT>0,00<DTASOF>20260731</LEDGERBAL>
        </STMTRS></STMTTRNRS></BANKMSGSRSV1>
        </OFX>
        """;

    // Two debits with the SAME amount (-1000) — only one matching installment exists.
    private const string DuplicateDebitsOfxContent = """
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
        <STMTTRN><TRNTYPE>DEBIT<DTPOSTED>20260710<TRNAMT>-1000.00<FITID>DUP-1<MEMO>PAGFOR PIX</STMTTRN>
        <STMTTRN><TRNTYPE>DEBIT<DTPOSTED>20260711<TRNAMT>-1000.00<FITID>DUP-2<MEMO>PAGFOR PIX</STMTTRN>
        </BANKTRANLIST>
        <LEDGERBAL><BALAMT>0.00<DTASOF>20260731</LEDGERBAL>
        </STMTRS></STMTTRNRS></BANKMSGSRSV1>
        </OFX>
        """;

    private static MultipartFormDataContent OfxFileRaw(string content, string filename = "extrato.ofx")
    {
        var mp = new MultipartFormDataContent();
        var bytes = new ByteArrayContent(Encoding.Latin1.GetBytes(content));
        bytes.Headers.ContentType = new MediaTypeHeaderValue("application/x-ofx");
        mp.Add(bytes, "file", filename);
        return mp;
    }

    private static string? WarningOf(JsonElement body) =>
        body.TryGetProperty("warning", out var w) && w.ValueKind == JsonValueKind.String ? w.GetString() : null;

    [Fact]
    public async Task ImportStatement_ParsesRealSantanderFormat_ptBRDecimals_Returns201()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);

        using var content = OfxFileRaw(SantanderOfxContent);
        var res = await client.PostAsync($"/api/statement-imports?bankAccountId={bankId}", content);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode); // formerly failed: 2650.000,00 threw a FormatException
        var body = await ReadJson(res);
        Assert.Equal(3, body.GetProperty("transactionCount").GetInt32());
        Assert.Equal("2026-06-12", body.GetProperty("periodStart").GetString());
        Assert.Equal("2026-06-12", body.GetProperty("periodEnd").GetString());

        // The pt-BR amounts must be parsed correctly (not ×100, not truncated).
        var listRes = await client.GetAsync($"/api/bank-transactions?bankAccountId={bankId}&pageSize=50");
        listRes.EnsureSuccessStatusCode();
        var amounts = (await ReadJson(listRes)).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("amount").GetDecimal()).ToList();
        Assert.Contains(-200.00m, amounts);
        Assert.Contains(2650000.00m, amounts);
        Assert.Contains(-1975.87m, amounts);
    }

    [Fact]
    public async Task ImportStatement_ForeignAccount_Imports_WithWarning()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId, ofxAccountId: "12345");

        using var content = OfxFileRaw(ForeignAccountOfxContent);
        var res = await client.PostAsync($"/api/statement-imports?bankAccountId={bankId}", content);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode); // warn-and-allow: still imported
        Assert.NotNull(WarningOf(await ReadJson(res)));
    }

    [Fact]
    public async Task ImportStatement_MatchingAccount_NoWarning()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        // The standard OfxFile()'s ACCTID is 12345; configure the account with the same OFX id.
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId, ofxAccountId: "12345");

        using var content = OfxFile();
        var res = await client.PostAsync($"/api/statement-imports?bankAccountId={bankId}", content);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.Null(WarningOf(await ReadJson(res)));
    }

    [Fact]
    public async Task PeriodSummary_GroupsByStatus_AndTotalsDebitCredit()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        await CreateApprovedPayableAsync(factory, client, seed, 1000.00m, "2026-07-12");
        await ImportAsync(client, bankId); // -1000 debit → Suggested; 2500 credit → Unmatched

        var res = await client.GetAsync($"/api/bank-transactions/period-summary?companyId={seed.CompanyId}&bankAccountId={bankId}");
        res.EnsureSuccessStatusCode();
        var body = await ReadJson(res);

        Assert.Equal(1, body.GetProperty("suggested").GetProperty("count").GetInt32());
        Assert.Equal(-1000.00m, body.GetProperty("suggested").GetProperty("total").GetDecimal());
        Assert.Equal(1, body.GetProperty("unmatched").GetProperty("count").GetInt32());
        Assert.Equal(2500.00m, body.GetProperty("unmatched").GetProperty("total").GetDecimal());
        Assert.Equal(-1000.00m, body.GetProperty("debitTotal").GetDecimal());
        Assert.Equal(2500.00m, body.GetProperty("creditTotal").GetDecimal());
    }

    [Fact]
    public async Task ListBankTransactions_DirectionFilter_SplitsDebitCredit()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        await ImportAsync(client, bankId);

        var debitRes = await client.GetAsync($"/api/bank-transactions?bankAccountId={bankId}&direction=Debit");
        debitRes.EnsureSuccessStatusCode();
        Assert.Equal(1, (await ReadJson(debitRes)).GetProperty("total").GetInt32());

        var creditRes = await client.GetAsync($"/api/bank-transactions?bankAccountId={bankId}&direction=Credit");
        creditRes.EnsureSuccessStatusCode();
        Assert.Equal(1, (await ReadJson(creditRes)).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task RunMatcher_DuplicateAmounts_SuggestsOnlyOne()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        // ONE installment of 1000 vs TWO -1000 debits → only one may be suggested (one-to-one pool).
        await CreateApprovedPayableAsync(factory, client, seed, 1000.00m, "2026-07-12");

        using var content = OfxFileRaw(DuplicateDebitsOfxContent);
        var res = await client.PostAsync($"/api/statement-imports?bankAccountId={bankId}", content);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var suggestedRes = await client.GetAsync($"/api/bank-transactions?bankAccountId={bankId}&matchStatus=Suggested");
        suggestedRes.EnsureSuccessStatusCode();
        Assert.Equal(1, (await ReadJson(suggestedRes)).GetProperty("total").GetInt32());

        var unmatchedRes = await client.GetAsync($"/api/bank-transactions?bankAccountId={bankId}&matchStatus=Unmatched");
        unmatchedRes.EnsureSuccessStatusCode();
        Assert.Equal(1, (await ReadJson(unmatchedRes)).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task ClearReconciliation_IgnoresOpenTransactionsOfDay_LeavesOtherDays()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        await CreateApprovedPayableAsync(factory, client, seed, 1000.00m, "2026-07-12");
        await ImportAsync(client, bankId); // -1000 debit (2026-07-10, Suggested); 2500 credit (2026-07-12, Unmatched)

        // Clear the debit's day → the open (Suggested) debit is ignored in bulk.
        var res = await client.PostAsJsonAsync("/api/bank-transactions/clear",
            new { companyId = seed.CompanyId, bankAccountId = bankId, date = "2026-07-10" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(1, (await ReadJson(res)).GetProperty("ignored").GetInt32());

        Assert.Equal(1, (await ReadJson(await client.GetAsync(
            $"/api/bank-transactions?bankAccountId={bankId}&matchStatus=Ignored"))).GetProperty("total").GetInt32());
        // The 2026-07-12 credit is another day → untouched.
        Assert.Equal(1, (await ReadJson(await client.GetAsync(
            $"/api/bank-transactions?bankAccountId={bankId}&matchStatus=Unmatched"))).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task ListBankTransactions_StageFilter_SplitsAnalysisAndResolved()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        await CreateApprovedPayableAsync(factory, client, seed, 1000.00m, "2026-07-12");
        await ImportAsync(client, bankId); // debit Suggested + credit Unmatched → both "analysis"

        async Task<int> StageTotal(string stage) =>
            (await ReadJson(await client.GetAsync($"/api/bank-transactions?bankAccountId={bankId}&stage={stage}")))
            .GetProperty("total").GetInt32();

        Assert.Equal(2, await StageTotal("analysis"));
        Assert.Equal(0, await StageTotal("resolved"));

        // Ignore the credit → it leaves "analysis" and enters "resolved".
        var credit = await SingleTransactionAsync(client, bankId, "Unmatched");
        var ignoreRes = await client.PostAsJsonAsync($"/api/bank-transactions/{credit.GetProperty("id").GetString()}/ignore", new { });
        Assert.Equal(HttpStatusCode.OK, ignoreRes.StatusCode);

        Assert.Equal(1, await StageTotal("analysis"));
        Assert.Equal(1, await StageTotal("resolved"));
    }
}
