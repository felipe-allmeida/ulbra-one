using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ulbra.One.IntegrationTests;

namespace Ulbra.One.IntegrationTests.Finance;

[Collection("Postgres")]
public class ReceivableEndpointsTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private sealed record Seed(string CompanyId, string CostCenterId, string CustomerId);

    /// <summary>Seeds a company, a cost center and a customer (the payer) via the public API.</summary>
    private static async Task<Seed> SeedAsync(HttpClient client)
    {
        var cnpj = TestDocuments.RandomCnpj();
        var companyRes = await client.PostAsJsonAsync("/api/companies", new { legalName = "ULBRA Teste", cnpj });
        companyRes.EnsureSuccessStatusCode();
        var companyId = (await ReadJson(companyRes)).GetProperty("id").GetString()!;

        var ccRes = await client.PostAsJsonAsync("/api/cost-centers",
            new { companyId, code = "CC1", name = "Mensalidades", annualBudget = 1_000_000m, autoApprovalLimit = 0m, unitIds = Array.Empty<string>() });
        ccRes.EnsureSuccessStatusCode();
        var costCenterId = (await ReadJson(ccRes)).GetProperty("id").GetString()!;

        var customerRes = await client.PostAsJsonAsync("/api/customers", new
        {
            companyId,
            personType = 1,
            name = "Hospital Universitário Ltda",
            tradeName = "HU",
            document = TestCnpj.New(),
            email = "financeiro@hu.com",
            phone = "5133334444",
            address = (object?)null,
            creditLimit = (decimal?)null,
        });
        customerRes.EnsureSuccessStatusCode();
        var customerId = (await ReadJson(customerRes)).GetProperty("id").GetString()!;

        return new Seed(companyId, costCenterId, customerId);
    }

    private static async Task<string> CreateBankAccountAsync(HttpClient client, string companyId, string accountNumber = "00012345")
    {
        var ledgerAccountId = await FinanceSeed.CreateAccountingAccountAsync(client);
        var res = await client.PostAsJsonAsync("/api/bank-accounts", new
        {
            companyId,
            bankCode = "033",
            bankName = "Banco Santander",
            agency = "1234",
            accountNumber,
            accountDigit = "6",
            accountType = "Checking",
            alias = "Conta " + accountNumber,
            openingBalance = 10_000m,
            ledgerAccountId,
        });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("id").GetString()!;
    }

    /// <summary>Creates a single-installment receivable of the given amount (born "AwaitingReceipt").</summary>
    private static async Task<string> CreateReceivableAsync(HttpClient client, Seed seed, decimal amount, string dueDate = "2026-06-30")
    {
        var createRes = await client.PostAsJsonAsync("/api/receivables", new
        {
            companyId = seed.CompanyId,
            payerId = seed.CustomerId,
            costCenterId = seed.CostCenterId,
            description = "Mensalidade",
            installments = new[] { new { dueDate, amount } },
        });
        createRes.EnsureSuccessStatusCode();
        return (await ReadJson(createRes)).GetProperty("id").GetString()!;
    }

    private static async Task<(string ReceivableId, string InstallmentId)> FirstInstallmentAsync(HttpClient client, string receivableId)
    {
        var body = await ReadJson(await client.GetAsync($"/api/receivables/{receivableId}"));
        var installmentId = body.GetProperty("installments")[0].GetProperty("id").GetString()!;
        return (receivableId, installmentId);
    }

    private static async Task<string> RegisterReceiptAsync(HttpClient client, string receivableId, string installmentId, string bankId, decimal amount)
    {
        var res = await client.PostAsJsonAsync($"/api/receivables/{receivableId}/installments/{installmentId}/receipts", new
        {
            receiptDate = "2026-06-05",
            amount,
            bankAccountId = bankId,
            method = "Pix",
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await ReadJson(res)).GetProperty("installments")[0].GetProperty("receipts")[0].GetProperty("id").GetString()!;
    }

    private static async Task<HttpResponseMessage> UpdateReceivableAsync(
        HttpClient client, string receivableId, Seed seed, string description, decimal amount, string dueDate = "2026-06-30") =>
        await client.PutAsJsonAsync($"/api/receivables/{receivableId}", new
        {
            payerId = seed.CustomerId,
            costCenterId = seed.CostCenterId,
            description,
            installments = new[] { new { dueDate, amount } },
        });

    // ---------------------------------------------------------------------------
    // Create / list
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Created_Receivable_Is_Born_AwaitingReceipt()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var receivableId = await CreateReceivableAsync(client, seed, 1500m);

        var body = await ReadJson(await client.GetAsync($"/api/receivables/{receivableId}"));
        Assert.Equal("AwaitingReceipt", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task List_Flags_Overdue_Installments()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        await CreateReceivableAsync(client, seed, 800m, dueDate: "2020-01-10"); // long past due

        // "Vencidas" tab semantics: not received + due before today.
        var body = await ReadJson(
            await client.GetAsync($"/api/receivables?companyId={seed.CompanyId}&received=false&dueTo=2020-12-31"));
        var items = body.GetProperty("items");
        Assert.True(items.GetArrayLength() >= 1);
        Assert.True(items[0].GetProperty("isOverdue").GetBoolean());
    }

    // ---------------------------------------------------------------------------
    // Cancel
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Cancelled_Receivable_Has_No_Overdue_Installments()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var receivableId = await CreateReceivableAsync(client, seed, 800m, dueDate: "2020-01-10"); // long past due

        var cancelRes = await client.PostAsJsonAsync($"/api/receivables/{receivableId}/cancel", new { reason = "test" });
        Assert.Equal(HttpStatusCode.OK, cancelRes.StatusCode);

        // Detail: header is Cancelled and the (still-Open) installment is no longer flagged overdue.
        var detail = await ReadJson(await client.GetAsync($"/api/receivables/{receivableId}"));
        Assert.Equal("Cancelled", detail.GetProperty("status").GetString());
        Assert.False(detail.GetProperty("installments")[0].GetProperty("isOverdue").GetBoolean());

        // List: the same row is not overdue either.
        var list = await ReadJson(
            await client.GetAsync($"/api/receivables?companyId={seed.CompanyId}&received=false&dueTo=2020-12-31"));
        var items = list.GetProperty("items");
        Assert.True(items.GetArrayLength() >= 1);
        Assert.False(items[0].GetProperty("isOverdue").GetBoolean());
    }

    // ---------------------------------------------------------------------------
    // Update
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Update_Changes_Description_And_Total()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var receivableId = await CreateReceivableAsync(client, seed, 1500m);

        var res = await UpdateReceivableAsync(client, receivableId, seed, "Mensalidade revisada", 2000m);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await ReadJson(await client.GetAsync($"/api/receivables/{receivableId}"));
        Assert.Equal("Mensalidade revisada", body.GetProperty("description").GetString());
        Assert.Equal(2000m, body.GetProperty("totalAmount").GetDecimal());
    }

    [Fact]
    public async Task Update_After_Receipt_Returns_409_NotEditable()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        var receivableId = await CreateReceivableAsync(client, seed, 1500m);
        var (_, installmentId) = await FirstInstallmentAsync(client, receivableId);
        await RegisterReceiptAsync(client, receivableId, installmentId, bankId, 1500m);

        var res = await UpdateReceivableAsync(client, receivableId, seed, "tentando editar", 1500m);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("receivable.not_editable", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Update_With_Inactive_Customer_Returns_400_CustomerInactive()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var receivableId = await CreateReceivableAsync(client, seed, 1500m);

        var deactivate = await client.PostAsync($"/api/customers/{seed.CustomerId}/deactivate", null);
        deactivate.EnsureSuccessStatusCode();

        var res = await UpdateReceivableAsync(client, receivableId, seed, "x", 1500m);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("receivable.customer_inactive", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    // ---------------------------------------------------------------------------
    // Register receipt
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Register_Full_Receipt_Settles_With_Exactly_One_Receipt()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        var receivableId = await CreateReceivableAsync(client, seed, 1500m);
        var (_, installmentId) = await FirstInstallmentAsync(client, receivableId);

        var res = await client.PostAsJsonAsync($"/api/receivables/{receivableId}/installments/{installmentId}/receipts", new
        {
            receiptDate = "2026-06-05",
            amount = 1500m,
            bankAccountId = bankId,
            method = "Pix",
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var body = await ReadJson(await client.GetAsync($"/api/receivables/{receivableId}"));
        Assert.Equal("Received", body.GetProperty("status").GetString());
        var installment = body.GetProperty("installments")[0];
        Assert.Equal("Received", installment.GetProperty("status").GetString());
        Assert.Equal(1, installment.GetProperty("receipts").GetArrayLength());
    }

    [Fact]
    public async Task Register_Partial_Receipt_Leaves_PartiallyReceived_And_AwaitingReceipt()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        var receivableId = await CreateReceivableAsync(client, seed, 1500m);
        var (_, installmentId) = await FirstInstallmentAsync(client, receivableId);

        var res = await client.PostAsJsonAsync($"/api/receivables/{receivableId}/installments/{installmentId}/receipts", new
        {
            receiptDate = "2026-06-05",
            amount = 500m,
            bankAccountId = bankId,
            method = "Pix",
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var body = await ReadJson(await client.GetAsync($"/api/receivables/{receivableId}"));
        Assert.Equal("AwaitingReceipt", body.GetProperty("status").GetString());
        Assert.Equal("PartiallyReceived", body.GetProperty("installments")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Register_With_BankAccount_Of_Another_Company_Returns_400()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var otherSeed = await SeedAsync(client);
        var otherBankId = await CreateBankAccountAsync(client, otherSeed.CompanyId);
        var receivableId = await CreateReceivableAsync(client, seed, 1500m);
        var (_, installmentId) = await FirstInstallmentAsync(client, receivableId);

        var res = await client.PostAsJsonAsync($"/api/receivables/{receivableId}/installments/{installmentId}/receipts", new
        {
            receiptDate = "2026-06-05",
            amount = 1500m,
            bankAccountId = otherBankId,
            method = "Pix",
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("receipt.bank_account_invalid", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Register_With_Zero_Amount_Returns_400()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        var receivableId = await CreateReceivableAsync(client, seed, 1500m);
        var (_, installmentId) = await FirstInstallmentAsync(client, receivableId);

        var res = await client.PostAsJsonAsync($"/api/receivables/{receivableId}/installments/{installmentId}/receipts", new
        {
            receiptDate = "2026-06-05",
            amount = 0m,
            bankAccountId = bankId,
            method = "Pix",
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("receipt.amount_invalid", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Register_Over_Outstanding_Balance_Returns_400_WouldExceedBalance()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        var receivableId = await CreateReceivableAsync(client, seed, 1000m);
        var (_, installmentId) = await FirstInstallmentAsync(client, receivableId);

        var res = await client.PostAsJsonAsync($"/api/receivables/{receivableId}/installments/{installmentId}/receipts", new
        {
            receiptDate = "2026-06-05",
            amount = 1200m,
            bankAccountId = bankId,
            method = "Pix",
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("receipt.would_exceed_balance", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Register_With_Future_Date_Returns_400_DateInFuture()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        var receivableId = await CreateReceivableAsync(client, seed, 1500m);
        var (_, installmentId) = await FirstInstallmentAsync(client, receivableId);

        var res = await client.PostAsJsonAsync($"/api/receivables/{receivableId}/installments/{installmentId}/receipts", new
        {
            receiptDate = "2999-01-01",
            amount = 1500m,
            bankAccountId = bankId,
            method = "Pix",
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("receipt.date_in_future", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Register_On_AlreadyReceived_Installment_Returns_409()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        var receivableId = await CreateReceivableAsync(client, seed, 1500m);
        var (_, installmentId) = await FirstInstallmentAsync(client, receivableId);
        await RegisterReceiptAsync(client, receivableId, installmentId, bankId, 1500m);

        var second = await client.PostAsJsonAsync($"/api/receivables/{receivableId}/installments/{installmentId}/receipts", new
        {
            receiptDate = "2026-06-06",
            amount = 100m,
            bankAccountId = bankId,
            method = "Pix",
        });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("receipt.installment_settled", (await ReadJson(second)).GetProperty("error_code").GetString());
    }

    // ---------------------------------------------------------------------------
    // Reverse receipt
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Reverse_Receipt_Reopens_Installment_And_Receivable()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        var receivableId = await CreateReceivableAsync(client, seed, 1500m);
        var (_, installmentId) = await FirstInstallmentAsync(client, receivableId);
        var receiptId = await RegisterReceiptAsync(client, receivableId, installmentId, bankId, 1500m);

        var reverseRes = await client.PostAsJsonAsync($"/api/receivables/{receivableId}/receipts/{receiptId}/reverse", new { reason = "estorno teste" });
        Assert.Equal(HttpStatusCode.OK, reverseRes.StatusCode);

        var body = await ReadJson(await client.GetAsync($"/api/receivables/{receivableId}"));
        Assert.Equal("AwaitingReceipt", body.GetProperty("status").GetString());
        var installment = body.GetProperty("installments")[0];
        Assert.Equal("Open", installment.GetProperty("status").GetString());
        var receipt = installment.GetProperty("receipts")[0];
        Assert.True(receipt.GetProperty("isReversed").GetBoolean());
        Assert.False(receipt.GetProperty("reversedAt").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task Reverse_AlreadyReversed_Returns_409()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        var receivableId = await CreateReceivableAsync(client, seed, 1500m);
        var (_, installmentId) = await FirstInstallmentAsync(client, receivableId);
        var receiptId = await RegisterReceiptAsync(client, receivableId, installmentId, bankId, 1500m);

        var first = await client.PostAsJsonAsync($"/api/receivables/{receivableId}/receipts/{receiptId}/reverse", new { reason = "primeiro" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var second = await client.PostAsJsonAsync($"/api/receivables/{receivableId}/receipts/{receiptId}/reverse", new { reason = "segundo" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("receipt.already_reversed", (await ReadJson(second)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Reverse_NonExistent_Receipt_Returns_404()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var receivableId = await CreateReceivableAsync(client, seed, 1500m);

        var res = await client.PostAsJsonAsync($"/api/receivables/{receivableId}/receipts/receipt_{Guid.NewGuid():N}/reverse", new { reason = "x" });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("receipt.not_found", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    // ---------------------------------------------------------------------------
    // Receipt proof + bank-account-in-use
    // ---------------------------------------------------------------------------

    private static MultipartFormDataContent FileContent(byte[] bytes, string fileName, string contentType)
    {
        var content = new MultipartFormDataContent();
        var fileBytes = new ByteArrayContent(bytes);
        fileBytes.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileBytes, "file", fileName);
        return content;
    }

    private static byte[] ValidPdf() =>
        Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n");

    [Fact]
    public async Task UploadProof_ValidPdf_Shows_HasProof_And_Download_Returns_Bytes()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        var receivableId = await CreateReceivableAsync(client, seed, 1500m);
        var (_, installmentId) = await FirstInstallmentAsync(client, receivableId);
        var receiptId = await RegisterReceiptAsync(client, receivableId, installmentId, bankId, 1500m);

        var pdf = ValidPdf();
        using var content = FileContent(pdf, "comprovante.pdf", "application/pdf");
        var uploadRes = await client.PostAsync($"/api/receivables/{receivableId}/receipts/{receiptId}/proof", content);
        Assert.Equal(HttpStatusCode.Created, uploadRes.StatusCode);
        Assert.True((await ReadJson(uploadRes)).GetProperty("hasProof").GetBoolean());

        var downloadRes = await client.GetAsync($"/api/receivables/{receivableId}/receipts/{receiptId}/proof");
        Assert.Equal(HttpStatusCode.OK, downloadRes.StatusCode);
        Assert.Equal(pdf, await downloadRes.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Delete_BankAccount_InUse_By_Receipt_Returns_409()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        var receivableId = await CreateReceivableAsync(client, seed, 1500m);
        var (_, installmentId) = await FirstInstallmentAsync(client, receivableId);
        await RegisterReceiptAsync(client, receivableId, installmentId, bankId, 1500m);

        var deleteRes = await client.DeleteAsync($"/api/bank-accounts/{bankId}");
        Assert.Equal(HttpStatusCode.Conflict, deleteRes.StatusCode);
        Assert.Equal("bank_account.in_use", (await ReadJson(deleteRes)).GetProperty("error_code").GetString());
    }
}
