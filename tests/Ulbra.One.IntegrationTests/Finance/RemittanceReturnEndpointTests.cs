using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Finance.Infrastructure.Cnab;
using Ulbra.One.IntegrationTests;

namespace Ulbra.One.IntegrationTests.Finance;

/// <summary>
/// Integration tests for the return conciliation HTTP endpoints (Task 5 / AEL-618):
///   POST /api/remittances/{id}/simulate-return   — build + download a synthetic .RET
///   POST /api/remittances/{id}/return             — upload + process a .RET file (multipart)
///   POST /api/remittances/{id}/lines/{lineId}/confirm-paid
///   POST /api/remittances/{id}/lines/{lineId}/mark-returned
/// </summary>
[Collection("Postgres")]
public class RemittanceReturnEndpointTests(PostgresFixture fixture)
{
    // -----------------------------------------------------------------------
    // JSON helper
    // -----------------------------------------------------------------------

    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    // -----------------------------------------------------------------------
    // Seed: one company + Santander 033 bank account + two approved payables
    // -----------------------------------------------------------------------

    private sealed record SeedResult(
        Guid CompanyId,
        string BankAccountPrefixedId,
        Guid BankAccountId);

    private static async Task<SeedResult> SeedAsync(ApiFactory factory, HttpClient client)
    {
        var cnpj = TestDocuments.RandomCnpj();

        var companyRes = await client.PostAsJsonAsync("/api/companies",
            new { legalName = "Empresa Ret Endpoint Test", cnpj });
        companyRes.EnsureSuccessStatusCode();
        var companyPrefixedId = (await ReadJson(companyRes)).GetProperty("id").GetString()!;
        var companyId = Guid.Parse(companyPrefixedId[(companyPrefixedId.LastIndexOf('_') + 1)..]);

        var ccRes = await client.PostAsJsonAsync("/api/cost-centers",
            new
            {
                companyId = companyPrefixedId,
                code = "CC-RET-EP",
                name = "Return EP TI",
                annualBudget = 1_000_000m,
                autoApprovalLimit = 0m,
                unitIds = Array.Empty<string>(),
            });
        ccRes.EnsureSuccessStatusCode();
        var costCenterId = (await ReadJson(ccRes)).GetProperty("id").GetString()!;

        // Vendor A — TED
        var vendorARes = await client.PostAsJsonAsync("/api/vendors", new
        {
            companyId = companyPrefixedId,
            personType = 1,
            name = "Fornecedor TED RetEP Ltda",
            tradeName = "TedRetEP",
            document = TestCnpj.New(),
            email = "tedretep@test.com",
            phone = "5133334444",
            address = (object?)null,
            paymentTerms = "30 dias",
            bankInfo = new { bankCode = "341", branch = "0001", account = "123456", accountDigit = "7", pixKey = "" },
        });
        vendorARes.EnsureSuccessStatusCode();
        var vendorAPrefixedId = (await ReadJson(vendorARes)).GetProperty("id").GetString()!;

        // Bank account: Santander 033 with agreement code
        await using var setupScope = factory.Services.CreateAsyncScope();
        var uow = setupScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var bankAccount = new BankAccount(companyId, "033", "Banco Santander", "1234",
            "00012345", "6", BankAccountType.Checking, "Conta Ret EP", null,
            cnabAgreementCode: "123456789012");
        bankAccount.SetAsDefaultPayer();
        await uow.AddAsync(bankAccount, CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);

        var bankAccountPrefixedId = $"bank_account_{bankAccount.Id:N}";

        // Payable A (TED, 1000)
        var payableARes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = companyPrefixedId,
            vendorId = vendorAPrefixedId,
            costCenterId,
            description = "Payable TED RetEP",
            installments = new[] { new { dueDate = "2026-06-01", amount = 1_000m } },
            type = "Product", // determined product — required before a payable can schedule
            paymentMethod = "Ted", // forma na parcela (AEL-933)
        });
        payableARes.EnsureSuccessStatusCode();
        var payableAPrefixedId = (await ReadJson(payableARes)).GetProperty("id").GetString()!;
        (await client.PostAsJsonAsync($"/api/payables/{payableAPrefixedId}/approve", new { })).EnsureSuccessStatusCode();
        await FinanceSeed.CompleteAccountingAsync(factory, client, payableAPrefixedId); // → Scheduled

        return new SeedResult(companyId, bankAccountPrefixedId, bankAccount.Id);
    }

    /// <summary>Generate a remittance and return its id prefixed string + guid.</summary>
    private static async Task<(string PrefixedId, Guid Id)> GenerateRemittanceAsync(
        HttpClient client, string bankAccountPrefixedId)
    {
        var createRes = await client.PostAsJsonAsync("/api/remittances", new
        {
            payingBankAccountId = bankAccountPrefixedId,
            dueCutoff = "2026-12-31",
        });
        createRes.EnsureSuccessStatusCode();
        var body = await ReadJson(createRes);
        var idStr = body.GetProperty("remittances")[0].GetProperty("id").GetString()!;
        return (idStr, Guid.Parse(idStr[(idStr.LastIndexOf('_') + 1)..]));
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Anonymous_Return_Endpoints_Return_401()
    {
        var factory = fixture.Api;
        var anon = factory.CreateClient();

        var remittanceId = $"remittance_{Guid.NewGuid():N}";

        // simulate-return
        var r1 = await anon.PostAsync($"/api/remittances/{remittanceId}/simulate-return", null);
        Assert.Equal(HttpStatusCode.Unauthorized, r1.StatusCode);

        // import return (multipart)
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Array.Empty<byte>()) { Headers = { ContentType = new MediaTypeHeaderValue("text/plain") } }, "file", "test.ret");
        var r2 = await anon.PostAsync($"/api/remittances/{remittanceId}/return", content);
        Assert.Equal(HttpStatusCode.Unauthorized, r2.StatusCode);

        // confirm-paid
        var lineId = $"remittance_line_{Guid.NewGuid():N}";
        var r3 = await anon.PostAsync($"/api/remittances/{remittanceId}/lines/{lineId}/confirm-paid", null);
        Assert.Equal(HttpStatusCode.Unauthorized, r3.StatusCode);

        // mark-returned
        var r4 = await anon.PostAsJsonAsync($"/api/remittances/{remittanceId}/lines/{lineId}/mark-returned",
            new { reason = "test" });
        Assert.Equal(HttpStatusCode.Unauthorized, r4.StatusCode);
    }

    [Fact]
    public async Task SimulateReturn_Returns_200_With_Ret_Bytes()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);
        var (remittanceId, _) = await GenerateRemittanceAsync(client, seed.BankAccountPrefixedId);

        var res = await client.PostAsync($"/api/remittances/{remittanceId}/simulate-return", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var bytes = await res.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 0, "Expected non-empty .RET file");

        // Must start with "033" (Santander bank code)
        var header = Encoding.Latin1.GetString(bytes, 0, Math.Min(3, bytes.Length));
        Assert.Equal("033", header);
    }

    [Fact]
    public async Task SimulateReturn_Then_ImportReturn_Marks_Lines_Paid()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);
        var (remittanceId, _) = await GenerateRemittanceAsync(client, seed.BankAccountPrefixedId);

        // Step 1: get synthetic .RET
        var simRes = await client.PostAsync($"/api/remittances/{remittanceId}/simulate-return", null);
        Assert.Equal(HttpStatusCode.OK, simRes.StatusCode);
        var retBytes = await simRes.Content.ReadAsByteArrayAsync();
        Assert.True(retBytes.Length > 0);

        // Step 2: upload .RET via multipart
        using var formContent = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(retBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        formContent.Add(fileContent, "file", "retorno.ret");

        var importRes = await client.PostAsync($"/api/remittances/{remittanceId}/return", formContent);
        var importBody = await importRes.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, importRes.StatusCode);

        var outcome = JsonDocument.Parse(importBody).RootElement;
        Assert.True(outcome.GetProperty("paid").GetInt32() >= 1, "Expected at least 1 paid line");

        // Step 3: GET remittance → lines should show Paid status
        var getRes = await client.GetAsync($"/api/remittances/{remittanceId}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var getBody = (await ReadJson(getRes));
        var lines = getBody.GetProperty("lines");
        Assert.True(lines.GetArrayLength() > 0);

        // All lines should now be Paid (simulate-return marks all as paid)
        for (int i = 0; i < lines.GetArrayLength(); i++)
        {
            var lineStatus = lines[i].GetProperty("status").GetString();
            Assert.Equal("Paid", lineStatus);
        }

        // Lines should expose effectiveDate, bankReference fields
        var firstLine = lines[0];
        Assert.True(firstLine.TryGetProperty("effectiveDate", out _), "Line should have effectiveDate");
        Assert.True(firstLine.TryGetProperty("bankReference", out _), "Line should have bankReference");
    }

    [Fact]
    public async Task ConfirmPaid_On_Pending_Line_Returns_200()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);
        var (remittanceId, remittanceGuid) = await GenerateRemittanceAsync(client, seed.BankAccountPrefixedId);

        // Get the remittance to find a line id
        var getRes = await client.GetAsync($"/api/remittances/{remittanceId}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var body = await ReadJson(getRes);
        var lines = body.GetProperty("lines");
        Assert.True(lines.GetArrayLength() > 0);

        var lineIdGuid = lines[0].GetProperty("id").GetGuid();
        var lineIdPrefixed = $"remittance_line_{lineIdGuid:N}";

        var confirmRes = await client.PostAsync(
            $"/api/remittances/{remittanceId}/lines/{lineIdPrefixed}/confirm-paid", null);
        Assert.Equal(HttpStatusCode.OK, confirmRes.StatusCode);

        // GET remittance → line should now be Paid
        var afterRes = await client.GetAsync($"/api/remittances/{remittanceId}");
        var afterBody = await ReadJson(afterRes);
        var afterLines = afterBody.GetProperty("lines");
        var matchedLine = Enumerable.Range(0, afterLines.GetArrayLength())
            .Select(i => afterLines[i])
            .FirstOrDefault(l => l.GetProperty("id").GetGuid() == lineIdGuid);

        Assert.Equal("Paid", matchedLine.GetProperty("status").GetString());
    }

    [Fact]
    public async Task MarkReturned_On_Pending_Line_Returns_200()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);
        var (remittanceId, _) = await GenerateRemittanceAsync(client, seed.BankAccountPrefixedId);

        // Get the remittance to find a line id
        var getRes = await client.GetAsync($"/api/remittances/{remittanceId}");
        var body = await ReadJson(getRes);
        var lines = body.GetProperty("lines");
        var lineIdGuid = lines[0].GetProperty("id").GetGuid();
        var lineIdPrefixed = $"remittance_line_{lineIdGuid:N}";

        var markRes = await client.PostAsJsonAsync(
            $"/api/remittances/{remittanceId}/lines/{lineIdPrefixed}/mark-returned",
            new { reason = "Test rejection reason" });
        Assert.Equal(HttpStatusCode.OK, markRes.StatusCode);

        // GET remittance → line should be Returned
        var afterRes = await client.GetAsync($"/api/remittances/{remittanceId}");
        var afterBody = await ReadJson(afterRes);
        var afterLines = afterBody.GetProperty("lines");
        var matchedLine = Enumerable.Range(0, afterLines.GetArrayLength())
            .Select(i => afterLines[i])
            .FirstOrDefault(l => l.GetProperty("id").GetGuid() == lineIdGuid);

        Assert.Equal("Returned", matchedLine.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ImportReturn_With_NonExistent_Remittance_Returns_200_With_Unmatched()
    {
        // The service processes the file and returns an outcome; unmatched references don't cause errors.
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);
        var (remittanceId, _) = await GenerateRemittanceAsync(client, seed.BankAccountPrefixedId);

        // Build a minimal .RET with a reference that won't match anything
        var retBytes = BuildMinimalRetFile("XXXXXXXXXXXXXXXX0000", 100m);
        using var formContent = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(retBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        formContent.Add(fileContent, "file", "unmatched.ret");

        var importRes = await client.PostAsync($"/api/remittances/{remittanceId}/return", formContent);
        Assert.Equal(HttpStatusCode.OK, importRes.StatusCode);

        var outcome = JsonDocument.Parse(await importRes.Content.ReadAsStringAsync()).RootElement;
        // unmatched ≥ 1 because the reference won't match
        Assert.True(outcome.GetProperty("unmatched").GetInt32() >= 1);
    }

    [Fact]
    public async Task ImportReturn_With_Divergent_Trailer_Returns_400()
    {
        // Detail actual-value (100) diverges from the declared lote trailer total (1000) → the file is
        // rejected as corrupted before any baixa (mirrors the legado blocking the operator's settlement).
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);
        var (remittanceId, _) = await GenerateRemittanceAsync(client, seed.BankAccountPrefixedId);

        var retBytes = BuildMinimalRetFile("XXXXXXXXXXXXXXXX0000", detailAmount: 100m, trailerAmount: 1_000m);
        using var formContent = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(retBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        formContent.Add(fileContent, "file", "divergent.ret");

        var importRes = await client.PostAsync($"/api/remittances/{remittanceId}/return", formContent);
        Assert.Equal(HttpStatusCode.BadRequest, importRes.StatusCode);

        var problem = JsonDocument.Parse(await importRes.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(ErrorCodes.Remittance.ReturnTrailerDivergent, problem.GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task PreviewCompanyReturn_Classifies_Rich_Entries_Without_Persisting()
    {
        // AEL-918: the dry-run preview matches a return against the paying account's pending lines and
        // returns rich per-title classification WITHOUT applying any baixa (lines stay Pending).
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);
        var (remittanceId, _) = await GenerateRemittanceAsync(client, seed.BankAccountPrefixedId);

        // A synthetic .RET that matches the generated remittance's lines (simulate marks them paid).
        var simRes = await client.PostAsync($"/api/remittances/{remittanceId}/simulate-return", null);
        Assert.Equal(HttpStatusCode.OK, simRes.StatusCode);
        var retBytes = await simRes.Content.ReadAsByteArrayAsync();

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(retBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(fileContent, "file", "retorno.ret");

        var res = await client.PostAsync($"/api/remittances/return/preview/{seed.BankAccountPrefixedId}", form);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await ReadJson(res);
        Assert.True(body.GetProperty("trailerOk").GetBoolean());
        Assert.True(body.GetProperty("counters").GetProperty("paid").GetInt32() >= 1, "Expected at least one paid entry.");
        var entries = body.GetProperty("entries");
        Assert.True(entries.GetArrayLength() >= 1);
        Assert.True(entries[0].TryGetProperty("classification", out _));
        Assert.True(entries[0].TryGetProperty("valorEfetivado", out _));

        // Dry-run: nothing persisted — every line is still Pending.
        var getBody = await ReadJson(await client.GetAsync($"/api/remittances/{remittanceId}"));
        var lines = getBody.GetProperty("lines");
        for (int i = 0; i < lines.GetArrayLength(); i++)
            Assert.Equal("Pending", lines[i].GetProperty("status").GetString());
    }

    [Fact]
    public async Task AutoConfirmCompanyReturn_Settles_Paid_Lines_On_Upload()
    {
        // AEL-918: the upload flow auto-confirms the "Paid" titles (baixa aplicada) so the operator never
        // has to click through titles the bank clearly settled. The synthetic return marks every line paid.
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);
        var (remittanceId, _) = await GenerateRemittanceAsync(client, seed.BankAccountPrefixedId);

        var simRes = await client.PostAsync($"/api/remittances/{remittanceId}/simulate-return", null);
        Assert.Equal(HttpStatusCode.OK, simRes.StatusCode);
        var retBytes = await simRes.Content.ReadAsByteArrayAsync();

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(retBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(fileContent, "file", "retorno.ret");

        var res = await client.PostAsync($"/api/remittances/return/auto-confirm/{seed.BankAccountPrefixedId}", form);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await ReadJson(res);
        // Paid lines were settled on upload → they surface as auto-confirmed and now read AlreadyProcessed.
        Assert.True(body.GetProperty("autoConfirmed").GetInt32() >= 1, "Expected at least one auto-confirmed line.");
        Assert.Equal(0, body.GetProperty("counters").GetProperty("paid").GetInt32());

        // Persisted: the matched lines are now Paid (baixa applied), not Pending.
        var getBody = await ReadJson(await client.GetAsync($"/api/remittances/{remittanceId}"));
        var lines = getBody.GetProperty("lines");
        Assert.Contains(
            Enumerable.Range(0, lines.GetArrayLength()).Select(i => lines[i].GetProperty("status").GetString()),
            s => s == "Paid");
    }

    [Fact]
    public async Task AutoConfirmCompanyReturn_Stores_The_File_And_Serves_It_From_History()
    {
        // The .RET the operator uploads has to survive the request: it is the bank's own document and the
        // only way to re-open a settlement later. The upload records it, the history lists it for that
        // paying account, and the download streams back the exact bytes that came in.
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);
        var (remittanceId, _) = await GenerateRemittanceAsync(client, seed.BankAccountPrefixedId);

        var simRes = await client.PostAsync($"/api/remittances/{remittanceId}/simulate-return", null);
        Assert.Equal(HttpStatusCode.OK, simRes.StatusCode);
        var retBytes = await simRes.Content.ReadAsByteArrayAsync();

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(retBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(fileContent, "file", "retorno-guardado.ret");

        var uploadRes = await client.PostAsync($"/api/remittances/return/auto-confirm/{seed.BankAccountPrefixedId}", form);
        Assert.Equal(HttpStatusCode.OK, uploadRes.StatusCode);

        var history = await ReadJson(await client.GetAsync($"/api/remittances/return/history/{seed.BankAccountPrefixedId}"));
        var items = history.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());

        var item = items[0];
        Assert.Equal("retorno-guardado.ret", item.GetProperty("fileName").GetString());
        Assert.True(item.GetProperty("applied").GetBoolean(), "Trailer reconciles, so the baixa was applied.");
        Assert.True(item.GetProperty("paid").GetInt32() >= 1, "Expected at least one settled line in the record.");
        Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("processedBy").GetString()));

        var returnId = item.GetProperty("id").GetString()!;
        var fileRes = await client.GetAsync($"/api/remittances/return/history/{returnId}/file");
        Assert.Equal(HttpStatusCode.OK, fileRes.StatusCode);
        Assert.Equal(retBytes, await fileRes.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task AutoConfirmCompanyReturn_With_Divergent_Trailer_Records_The_File_As_NotApplied()
    {
        // A file whose trailer does not reconcile applies nothing — and that is exactly the file the
        // operator will need to send back to the bank, so it is recorded too, flagged as not applied.
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);
        await GenerateRemittanceAsync(client, seed.BankAccountPrefixedId);

        var retBytes = BuildMinimalRetFile("XXXXXXXXXXXXXXXX0000", detailAmount: 100m, trailerAmount: 1_000m);
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(retBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(fileContent, "file", "divergente.ret");

        var uploadRes = await client.PostAsync($"/api/remittances/return/auto-confirm/{seed.BankAccountPrefixedId}", form);
        Assert.Equal(HttpStatusCode.OK, uploadRes.StatusCode);

        var history = await ReadJson(await client.GetAsync($"/api/remittances/return/history/{seed.BankAccountPrefixedId}"));
        var items = history.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("divergente.ret", items[0].GetProperty("fileName").GetString());
        Assert.False(items[0].GetProperty("applied").GetBoolean(), "Divergent trailer applies nothing.");
    }

    [Fact]
    public async Task PreviewCompanyReturn_Leaves_The_History_Empty()
    {
        // The preview is a dry run — and the tab re-runs it after every manual line action. Recording it
        // would fill the history with a row per click.
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);
        var (remittanceId, _) = await GenerateRemittanceAsync(client, seed.BankAccountPrefixedId);

        var simRes = await client.PostAsync($"/api/remittances/{remittanceId}/simulate-return", null);
        Assert.Equal(HttpStatusCode.OK, simRes.StatusCode);
        var retBytes = await simRes.Content.ReadAsByteArrayAsync();

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(retBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(fileContent, "file", "so-preview.ret");

        var previewRes = await client.PostAsync($"/api/remittances/return/preview/{seed.BankAccountPrefixedId}", form);
        Assert.Equal(HttpStatusCode.OK, previewRes.StatusCode);

        var history = await ReadJson(await client.GetAsync($"/api/remittances/return/history/{seed.BankAccountPrefixedId}"));
        Assert.Equal(0, history.GetProperty("items").GetArrayLength());
    }
    // -----------------------------------------------------------------------
    // Minimal .RET file builder (mirrors RemittanceReturnServiceTests helpers)
    // -----------------------------------------------------------------------

    private static byte[] BuildMinimalRetFile(string reference, decimal amount)
        => BuildMinimalRetFile(reference, amount, amount);

    private static byte[] BuildMinimalRetFile(string reference, decimal detailAmount, decimal trailerAmount)
    {
        var refPadded = (reference.Length < 20 ? reference.PadRight(20) : reference[..20]);
        var sb = new StringBuilder();
        sb.Append(BuildFileHeader()); sb.Append("\r\n");
        sb.Append(BuildBatchHeader()); sb.Append("\r\n");
        sb.Append(BuildSegmentA(refPadded, new DateOnly(2026, 6, 20), detailAmount, "BD        ", 1)); sb.Append("\r\n");
        sb.Append(BuildSegmentB(2)); sb.Append("\r\n");
        sb.Append(BuildBatchTrailer(trailerAmount)); sb.Append("\r\n");
        sb.Append(BuildFileTrailer()); sb.Append("\r\n");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    private static string BuildFileHeader()
    {
        var b = new CnabRecordBuilder(240);
        b.Numeric(1, 3, "033");
        b.Numeric(4, 4, "0");
        b.Numeric(8, 1, "0");
        b.Blank(9, 9);
        b.Numeric(18, 1, "2");
        b.Numeric(19, 14, "0");
        b.Alpha(33, 20, "0033555599999999");
        b.Numeric(53, 5, "0");
        b.Blank(58, 1);
        b.Numeric(59, 12, "0");
        b.Alpha(71, 1, "0");
        b.Blank(72, 1);
        b.Alpha(73, 30, "EMPRESA RET ENDPOINT TEST");
        b.Alpha(103, 30, "BANCO SANTANDER");
        b.Blank(133, 10);
        b.Numeric(143, 1, "2");
        b.Numeric(144, 8, "20062026");
        b.Numeric(152, 6, "100000");
        b.Numeric(158, 6, "1");
        b.Numeric(164, 3, "060");
        b.Numeric(167, 5, "0");
        b.Blank(172, 20);
        b.Blank(192, 20);
        b.Blank(212, 19);
        b.Blank(231, 10);
        return b.BuildComplete();
    }

    private static string BuildBatchHeader()
    {
        var b = new CnabRecordBuilder(240);
        b.Numeric(1, 3, "033");
        b.Numeric(4, 4, "1");
        b.Numeric(8, 1, "1");
        b.Alpha(9, 1, "C");
        b.Numeric(10, 2, "20");
        b.Numeric(12, 2, "03");
        b.Numeric(14, 3, "031");
        b.Blank(17, 1);
        b.Numeric(18, 1, "2");
        b.Numeric(19, 14, "0");
        b.Alpha(33, 20, "0033555599999999");
        b.Numeric(53, 5, "0");
        b.Blank(58, 1);
        b.Numeric(59, 12, "0");
        b.Alpha(71, 1, "0");
        b.Blank(72, 1);
        b.Alpha(73, 30, "EMPRESA RET ENDPOINT TEST");
        b.Blank(103, 40);
        b.Blank(143, 30);
        b.Numeric(173, 5, "0");
        b.Blank(178, 15);
        b.Blank(193, 20);
        b.Numeric(213, 5, "0");
        b.Numeric(218, 3, "0");
        b.Blank(221, 2);
        b.Blank(223, 8);
        b.Blank(231, 10);
        return b.BuildComplete();
    }

    private static string BuildSegmentA(string reference20, DateOnly? actualDate, decimal actualValue, string occurrences10, int seq)
    {
        var b = new CnabRecordBuilder(240);
        b.Numeric(1, 3, "033");
        b.Numeric(4, 4, "1");
        b.Numeric(8, 1, "3");
        b.Numeric(9, 5, seq);
        b.Alpha(14, 1, "A");
        b.Numeric(15, 1, "0");
        b.Numeric(16, 2, "0");
        b.Numeric(18, 3, "18");
        b.Numeric(21, 3, "341");
        b.Numeric(24, 5, "0001");
        b.Blank(29, 1);
        b.Numeric(30, 12, "123456");
        b.Alpha(42, 1, "7");
        b.Blank(43, 1);
        b.Alpha(44, 30, "FORNECEDOR TEST", allowTruncate: true);
        b.Alpha(74, 20, reference20[..20], allowTruncate: true);
        b.Date(94, 8, new DateOnly(2026, 6, 1));
        b.Alpha(102, 3, "BRL");
        b.Numeric(105, 15, "0");
        b.Money(120, 15, 1_000m);
        b.Alpha(135, 20, "BANKREFEP01         ", allowTruncate: true);
        if (actualDate.HasValue)
            b.Date(155, 8, actualDate.Value);
        else
            b.Numeric(155, 8, "0");
        b.Money(163, 15, actualValue);
        b.Blank(178, 40);
        b.Blank(218, 2);
        b.Alpha(220, 5, "00001");
        b.Blank(225, 2);
        b.Blank(227, 3);
        b.Numeric(230, 1, "0");
        b.Alpha(231, 10, occurrences10[..10], allowTruncate: true);
        return b.BuildComplete();
    }

    private static string BuildSegmentB(int seq)
    {
        var b = new CnabRecordBuilder(240);
        b.Numeric(1, 3, "033");
        b.Numeric(4, 4, "1");
        b.Numeric(8, 1, "3");
        b.Numeric(9, 5, seq);
        b.Alpha(14, 1, "B");
        b.Numeric(15, 2, "0");
        b.Blank(17, 1);
        b.Numeric(18, 1, "2");
        b.Numeric(19, 14, "0");
        b.Blank(33, 35);
        b.Blank(68, 60);
        b.Blank(128, 99);
        b.Blank(227, 6);
        b.Blank(233, 8);
        return b.BuildComplete();
    }

    // Trailer total must reconcile with the summed detail actual-values (pos 163), or the import is
    // rejected as a corrupted file. Parametrized by the same amount to keep the fixture self-consistent.
    private static string BuildBatchTrailer(decimal totalAmount)
    {
        var b = new CnabRecordBuilder(240);
        b.Numeric(1, 3, "033");
        b.Numeric(4, 4, "1");
        b.Numeric(8, 1, "5");
        b.Blank(9, 9);
        b.Numeric(18, 6, "4");
        b.Money(24, 18, totalAmount);
        b.Numeric(42, 18, "0");
        b.Numeric(60, 6, "0");
        b.Blank(66, 165);
        b.Blank(231, 10);
        return b.BuildComplete();
    }

    private static string BuildFileTrailer()
    {
        var b = new CnabRecordBuilder(240);
        b.Numeric(1, 3, "033");
        b.Numeric(4, 4, "9999");
        b.Numeric(8, 1, "9");
        b.Blank(9, 9);
        b.Numeric(18, 6, "1");
        b.Numeric(24, 6, "6");
        b.Blank(30, 6);
        b.Blank(36, 205);
        return b.BuildComplete();
    }
}
