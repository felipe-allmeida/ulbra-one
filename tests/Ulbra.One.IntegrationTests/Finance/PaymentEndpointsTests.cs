using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.IntegrationTests.Finance;

[Collection("Postgres")]
public class PaymentEndpointsTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private sealed record Seed(string CompanyId, string CostCenterId, string VendorId);

    /// <summary>Seeds a company, a cost center (with the given auto-approval limit) and a vendor via the public API.</summary>
    private static async Task<Seed> SeedAsync(HttpClient client, decimal autoApprovalLimit = 0)
    {
        var cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        var companyRes = await client.PostAsJsonAsync("/api/companies", new { legalName = "ULBRA Teste", cnpj });
        companyRes.EnsureSuccessStatusCode();
        var companyId = (await ReadJson(companyRes)).GetProperty("id").GetString()!;

        var ccRes = await client.PostAsJsonAsync("/api/cost-centers",
            new { companyId, code = "CC1", name = "Marketing", annualBudget = 1_000_000m, autoApprovalLimit, unitIds = Array.Empty<string>() });
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

    private static async Task<string> CreateBankAccountAsync(HttpClient client, string companyId, string accountNumber = "00012345")
    {
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
        });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("id").GetString()!;
    }

    /// <summary>Creates a single-installment payable of the given amount and (optionally) approves it.</summary>
    private static async Task<string> CreatePayableAsync(HttpClient client, Seed seed, decimal amount, bool approve = true)
    {
        var createRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Para pagar",
            installments = new[] { new { dueDate = "2026-07-10", amount } },
            isService = false, // determined product — required before a payable can schedule
        });
        createRes.EnsureSuccessStatusCode();
        var payableId = (await ReadJson(createRes)).GetProperty("id").GetString()!;

        if (approve)
        {
            var approveRes = await client.PostAsJsonAsync($"/api/payables/{payableId}/approve", new { });
            approveRes.EnsureSuccessStatusCode();
        }
        return payableId;
    }

    private static async Task<(string PayableId, string InstallmentId)> FirstInstallmentAsync(HttpClient client, string payableId)
    {
        var body = await ReadJson(await client.GetAsync($"/api/payables/{payableId}"));
        var installmentId = body.GetProperty("installments")[0].GetProperty("id").GetString()!;
        return (payableId, installmentId);
    }

    // ---------------------------------------------------------------------------
    // Register payment
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Register_Full_Payment_Settles_Installment_And_Payable_With_Exactly_One_Payment()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        var payableId = await CreatePayableAsync(client, seed, 1500m);
        var (_, installmentId) = await FirstInstallmentAsync(client, payableId);

        var res = await client.PostAsJsonAsync($"/api/payables/{payableId}/installments/{installmentId}/payments", new
        {
            paymentDate = "2026-07-09",
            amount = 1500m,
            bankAccountId = bankId,
            method = "Pix",
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var body = await ReadJson(await client.GetAsync($"/api/payables/{payableId}"));
        Assert.Equal("Paid", body.GetProperty("status").GetString());
        var installment = body.GetProperty("installments")[0];
        Assert.Equal("Paid", installment.GetProperty("status").GetString());
        // Exactly ONE payment — no dup (double insert) and no missing (zero insert).
        Assert.Equal(1, installment.GetProperty("payments").GetArrayLength());
    }

    [Fact]
    public async Task Register_Partial_Payment_Leaves_Installment_PartiallyPaid_And_Payable_Approved()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        var payableId = await CreatePayableAsync(client, seed, 1500m);
        var (_, installmentId) = await FirstInstallmentAsync(client, payableId);

        var res = await client.PostAsJsonAsync($"/api/payables/{payableId}/installments/{installmentId}/payments", new
        {
            paymentDate = "2026-07-09",
            amount = 500m,
            bankAccountId = bankId,
            method = "Pix",
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var body = await ReadJson(await client.GetAsync($"/api/payables/{payableId}"));
        Assert.Equal("Scheduled", body.GetProperty("status").GetString());
        Assert.Equal("PartiallyPaid", body.GetProperty("installments")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Register_On_NonApproved_Payable_Returns_400_NotPayable()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client, autoApprovalLimit: 1000m);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        // Above the auto-approval limit → stays PendingApproval (not approved).
        var payableId = await CreatePayableAsync(client, seed, 9000m, approve: false);
        var (_, installmentId) = await FirstInstallmentAsync(client, payableId);

        var res = await client.PostAsJsonAsync($"/api/payables/{payableId}/installments/{installmentId}/payments", new
        {
            paymentDate = "2026-07-09",
            amount = 100m,
            bankAccountId = bankId,
            method = "Pix",
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Payable.NotPayable", (await ReadJson(res)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Register_With_BankAccount_Of_Another_Company_Returns_400_BankAccountInvalid()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var otherSeed = await SeedAsync(client);
        var otherBankId = await CreateBankAccountAsync(client, otherSeed.CompanyId);
        var payableId = await CreatePayableAsync(client, seed, 1500m);
        var (_, installmentId) = await FirstInstallmentAsync(client, payableId);

        var res = await client.PostAsJsonAsync($"/api/payables/{payableId}/installments/{installmentId}/payments", new
        {
            paymentDate = "2026-07-09",
            amount = 1500m,
            bankAccountId = otherBankId,
            method = "Pix",
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Payment.BankAccountInvalid", (await ReadJson(res)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Register_With_Zero_Amount_Returns_400_AmountInvalid()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        var payableId = await CreatePayableAsync(client, seed, 1500m);
        var (_, installmentId) = await FirstInstallmentAsync(client, payableId);

        var res = await client.PostAsJsonAsync($"/api/payables/{payableId}/installments/{installmentId}/payments", new
        {
            paymentDate = "2026-07-09",
            amount = 0m,
            bankAccountId = bankId,
            method = "Pix",
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Payment.AmountInvalid", (await ReadJson(res)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Register_With_NonExistent_InstallmentId_Returns_404_InstallmentNotFound()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        var payableId = await CreatePayableAsync(client, seed, 1500m);

        var nonExistentInstallmentId = $"installment_{Guid.NewGuid():N}";
        var res = await client.PostAsJsonAsync($"/api/payables/{payableId}/installments/{nonExistentInstallmentId}/payments", new
        {
            paymentDate = "2026-07-09",
            amount = 1500m,
            bankAccountId = bankId,
            method = "Pix",
        });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("Payable.InstallmentNotFound", (await ReadJson(res)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Register_On_AlreadyPaid_Installment_Returns_409_InstallmentSettled()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        var payableId = await CreatePayableAsync(client, seed, 1500m);
        var (_, installmentId) = await FirstInstallmentAsync(client, payableId);

        var first = await client.PostAsJsonAsync($"/api/payables/{payableId}/installments/{installmentId}/payments", new
        {
            paymentDate = "2026-07-09",
            amount = 1500m,
            bankAccountId = bankId,
            method = "Pix",
        });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync($"/api/payables/{payableId}/installments/{installmentId}/payments", new
        {
            paymentDate = "2026-07-10",
            amount = 100m,
            bankAccountId = bankId,
            method = "Pix",
        });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("Payment.InstallmentSettled", (await ReadJson(second)).GetProperty("title").GetString());
    }

    // ---------------------------------------------------------------------------
    // Reverse payment
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Reverse_Payment_Reopens_Installment_And_Payable()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        var payableId = await CreatePayableAsync(client, seed, 1500m);
        var (_, installmentId) = await FirstInstallmentAsync(client, payableId);

        var payRes = await client.PostAsJsonAsync($"/api/payables/{payableId}/installments/{installmentId}/payments", new
        {
            paymentDate = "2026-07-09",
            amount = 1500m,
            bankAccountId = bankId,
            method = "Pix",
        });
        Assert.Equal(HttpStatusCode.Created, payRes.StatusCode);
        var payBody = await ReadJson(payRes);
        var paymentId = payBody.GetProperty("installments")[0].GetProperty("payments")[0].GetProperty("id").GetString()!;

        var reverseRes = await client.PostAsJsonAsync($"/api/payables/{payableId}/payments/{paymentId}/reverse", new { reason = "Estorno teste" });
        Assert.Equal(HttpStatusCode.OK, reverseRes.StatusCode);

        var body = await ReadJson(await client.GetAsync($"/api/payables/{payableId}"));
        Assert.Equal("Scheduled", body.GetProperty("status").GetString());
        var installment = body.GetProperty("installments")[0];
        Assert.Equal("Open", installment.GetProperty("status").GetString());
        Assert.True(installment.GetProperty("payments")[0].GetProperty("isReversed").GetBoolean());
    }

    [Fact]
    public async Task Reverse_AlreadyReversed_Returns_409_AlreadyReversed()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        var payableId = await CreatePayableAsync(client, seed, 1500m);
        var (_, installmentId) = await FirstInstallmentAsync(client, payableId);

        var payRes = await client.PostAsJsonAsync($"/api/payables/{payableId}/installments/{installmentId}/payments", new
        {
            paymentDate = "2026-07-09",
            amount = 1500m,
            bankAccountId = bankId,
            method = "Pix",
        });
        var paymentId = (await ReadJson(payRes)).GetProperty("installments")[0].GetProperty("payments")[0].GetProperty("id").GetString()!;

        var first = await client.PostAsJsonAsync($"/api/payables/{payableId}/payments/{paymentId}/reverse", new { reason = "primeiro" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsJsonAsync($"/api/payables/{payableId}/payments/{paymentId}/reverse", new { reason = "segundo" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("Payment.AlreadyReversed", (await ReadJson(second)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Reverse_NonExistent_Payment_Returns_404_NotFound()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var payableId = await CreatePayableAsync(client, seed, 1500m);

        var res = await client.PostAsJsonAsync($"/api/payables/{payableId}/payments/payment_{Guid.NewGuid():N}/reverse", new { reason = "x" });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("Payment.NotFound", (await ReadJson(res)).GetProperty("title").GetString());
    }

    // ---------------------------------------------------------------------------
    // Payment proof (comprovante) upload/download
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

    private static async Task<string> RegisterPaymentAsync(HttpClient client, string payableId, string installmentId, string bankId, decimal amount)
    {
        var res = await client.PostAsJsonAsync($"/api/payables/{payableId}/installments/{installmentId}/payments", new
        {
            paymentDate = "2026-07-09",
            amount,
            bankAccountId = bankId,
            method = "Pix",
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await ReadJson(res)).GetProperty("installments")[0].GetProperty("payments")[0].GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task GetPayable_Returns_Payments_Under_Installments()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        var payableId = await CreatePayableAsync(client, seed, 1500m);
        var (_, installmentId) = await FirstInstallmentAsync(client, payableId);
        var paymentId = await RegisterPaymentAsync(client, payableId, installmentId, bankId, 1500m);

        var body = await ReadJson(await client.GetAsync($"/api/payables/{payableId}"));
        var payments = body.GetProperty("installments")[0].GetProperty("payments");
        Assert.Equal(1, payments.GetArrayLength());
        Assert.Equal(paymentId, payments[0].GetProperty("id").GetString());
        Assert.False(payments[0].GetProperty("hasProof").GetBoolean());
    }

    [Fact]
    public async Task UploadProof_ValidPdf_Returns_201_And_GetPayable_Shows_HasProof_And_Download_Returns_Bytes()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        var payableId = await CreatePayableAsync(client, seed, 1500m);
        var (_, installmentId) = await FirstInstallmentAsync(client, payableId);
        var paymentId = await RegisterPaymentAsync(client, payableId, installmentId, bankId, 1500m);

        var pdf = ValidPdf();
        using var content = FileContent(pdf, "comprovante.pdf", "application/pdf");
        var uploadRes = await client.PostAsync($"/api/payables/{payableId}/payments/{paymentId}/proof", content);
        Assert.Equal(HttpStatusCode.Created, uploadRes.StatusCode);
        Assert.True((await ReadJson(uploadRes)).GetProperty("hasProof").GetBoolean());

        var body = await ReadJson(await client.GetAsync($"/api/payables/{payableId}"));
        Assert.True(body.GetProperty("installments")[0].GetProperty("payments")[0].GetProperty("hasProof").GetBoolean());

        var downloadRes = await client.GetAsync($"/api/payables/{payableId}/payments/{paymentId}/proof");
        Assert.Equal(HttpStatusCode.OK, downloadRes.StatusCode);
        Assert.Equal(pdf, await downloadRes.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task UploadProof_To_NonExistent_Payment_Returns_404()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var payableId = await CreatePayableAsync(client, seed, 1500m);

        using var content = FileContent(ValidPdf(), "comprovante.pdf", "application/pdf");
        var res = await client.PostAsync($"/api/payables/{payableId}/payments/payment_{Guid.NewGuid():N}/proof", content);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("Payment.NotFound", (await ReadJson(res)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task UploadProof_Oversized_Returns_400_TooLarge()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        var payableId = await CreatePayableAsync(client, seed, 1500m);
        var (_, installmentId) = await FirstInstallmentAsync(client, payableId);
        var paymentId = await RegisterPaymentAsync(client, payableId, installmentId, bankId, 1500m);

        var big = new byte[26 * 1024 * 1024];
        Encoding.ASCII.GetBytes("%PDF-1.4\n").CopyTo(big, 0);
        using var content = FileContent(big, "grande.pdf", "application/pdf");
        var res = await client.PostAsync($"/api/payables/{payableId}/payments/{paymentId}/proof", content);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("File.TooLarge", (await ReadJson(res)).GetProperty("title").GetString());
    }

    // ---------------------------------------------------------------------------
    // Block manual baixa on InRemittance installment (I2)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Register_On_InRemittance_Installment_Returns_409_InstallmentInRemittance()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        // Seed: company + cost center + vendor with TED bank info.
        var cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        var companyRes = await client.PostAsJsonAsync("/api/companies", new { legalName = "Empresa Remessa Block Test", cnpj });
        companyRes.EnsureSuccessStatusCode();
        var companyPrefixedId = (await ReadJson(companyRes)).GetProperty("id").GetString()!;
        var companyId = Guid.Parse(companyPrefixedId[(companyPrefixedId.LastIndexOf('_') + 1)..]);

        var ccRes = await client.PostAsJsonAsync("/api/cost-centers",
            new { companyId = companyPrefixedId, code = "CC-BLK", name = "Block Test", annualBudget = 1_000_000m, autoApprovalLimit = 0m, unitIds = Array.Empty<string>() });
        ccRes.EnsureSuccessStatusCode();
        var costCenterId = (await ReadJson(ccRes)).GetProperty("id").GetString()!;

        var vendorRes = await client.PostAsJsonAsync("/api/vendors", new
        {
            companyId = companyPrefixedId,
            personType = 1,
            name = "Fornecedor TED Block Ltda",
            tradeName = "FornecedorTEDBlock",
            document = "11.222.333/0001-81",
            email = "tedblock@test.com",
            phone = "5133334444",
            address = (object?)null,
            paymentTerms = "30 dias",
            bankInfo = new { bankCode = "341", branch = "0001", account = "123456", accountDigit = "7", pixKey = "" },
        });
        vendorRes.EnsureSuccessStatusCode();
        var vendorPrefixedId = (await ReadJson(vendorRes)).GetProperty("id").GetString()!;

        // Bank account: Santander 033 with agreement code + TED default (must be created via DI as in RemittanceEndpointTests).
        await using var setupScope = factory.Services.CreateAsyncScope();
        var uow = setupScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var bankAccount = new BankAccount(companyId, "033", "Banco Santander", "1234",
            "00099999", "6", BankAccountType.Checking, "Conta Block Test", null,
            cnabAgreementCode: "999999999999");
        bankAccount.SetAsDefaultPayer(PaymentMethod.Ted);
        await uow.AddAsync(bankAccount, CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);
        var bankAccountPrefixedId = $"bank_account_{bankAccount.Id:N}";

        // Payable with past due date so it's picked up by remittance generation.
        var payableRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = companyPrefixedId,
            vendorId = vendorPrefixedId,
            costCenterId,
            description = "Payable InRemittance Block",
            installments = new[] { new { dueDate = "2026-06-01", amount = 500m } },
            isService = false, // determined product — required before a payable can schedule
        });
        payableRes.EnsureSuccessStatusCode();
        var payableId = (await ReadJson(payableRes)).GetProperty("id").GetString()!;
        var approveRes = await client.PostAsJsonAsync($"/api/payables/{payableId}/approve", new { });
        approveRes.EnsureSuccessStatusCode();

        var (_, installmentId) = await FirstInstallmentAsync(client, payableId);

        // Generate remittance — this sets the installment to InRemittance.
        var remittanceRes = await client.PostAsJsonAsync("/api/remittances", new
        {
            payingBankAccountId = bankAccountPrefixedId,
            dueCutoff = "2026-06-30",
        });
        Assert.Equal(HttpStatusCode.Created, remittanceRes.StatusCode);

        // Verify the installment is now InRemittance.
        var payableBody = await ReadJson(await client.GetAsync($"/api/payables/{payableId}"));
        Assert.Equal("InRemittance", payableBody.GetProperty("installments")[0].GetProperty("status").GetString());

        // Attempt manual payment → must return 409 InRemittance.
        var payRes = await client.PostAsJsonAsync($"/api/payables/{payableId}/installments/{installmentId}/payments", new
        {
            paymentDate = "2026-06-25",
            amount = 500m,
            bankAccountId = bankAccountPrefixedId,
            method = "Ted",
        });
        Assert.Equal(HttpStatusCode.Conflict, payRes.StatusCode);
        Assert.Equal("Payment.InstallmentInRemittance", (await ReadJson(payRes)).GetProperty("title").GetString());
    }

    // ---------------------------------------------------------------------------
    // BankAccount in-use (carried from T5)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Delete_BankAccount_InUse_By_Payment_Returns_409_InUse()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        var payableId = await CreatePayableAsync(client, seed, 1500m);
        var (_, installmentId) = await FirstInstallmentAsync(client, payableId);

        var payRes = await client.PostAsJsonAsync($"/api/payables/{payableId}/installments/{installmentId}/payments", new
        {
            paymentDate = "2026-07-09",
            amount = 1500m,
            bankAccountId = bankId,
            method = "Pix",
        });
        Assert.Equal(HttpStatusCode.Created, payRes.StatusCode);

        var deleteRes = await client.DeleteAsync($"/api/bank-accounts/{bankId}");
        Assert.Equal(HttpStatusCode.Conflict, deleteRes.StatusCode);
        Assert.Equal("BankAccount.InUse", (await ReadJson(deleteRes)).GetProperty("title").GetString());
    }

    // ---------------------------------------------------------------------------
    // Manual baixa uses NetAmount (withholding guard, AEL-619)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Creates a payable with an IRRF withholding via domain (avoids HTTP which does not expose
    /// withholdings) so that installment.NetAmount &lt; installment.Amount.
    /// Returns (payablePrefixedId, installmentPrefixedId).
    /// </summary>
    private static async Task<(string PayablePrefixedId, string InstallmentPrefixedId)> CreateWithheldPayableAsync(
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
            counterpartyName: "Fornecedor Retencao Baixa",
            documentNumber: null,
            issueDate: null,
            description: "Payable with IRRF for manual baixa test",
            costCenterId: ccId,
            categoryId: null,
            installments: [installment]);

        payable.SetWithholdings([
            (WithholdingTaxType.Irrf, BaseAmount: grossAmount, Rate: null, Amount: withholdingAmount)
        ]);
        payable.Approve("test-user", clock.GetUtcNow());

        await uow.AddAsync(payable, CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);

        var payablePrefixedId = $"payable_{payable.Id:N}";
        var installmentPrefixedId = $"installment_{installment.Id:N}";
        return (payablePrefixedId, installmentPrefixedId);
    }

    [Fact]
    public async Task Register_NetAmount_On_Withheld_Payable_Settles_Installment_And_Payable()
    {
        // Gross 1000, IRRF 150 → NetAmount = 850; bank owes 850 in cash.
        const decimal gross = 1000m;
        const decimal withheld = 150m;
        const decimal net = gross - withheld; // 850

        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        var (payableId, installmentId) = await CreateWithheldPayableAsync(factory, seed, gross, withheld);

        var res = await client.PostAsJsonAsync($"/api/payables/{payableId}/installments/{installmentId}/payments", new
        {
            paymentDate = "2026-07-10",
            amount = net,
            bankAccountId = bankId,
            method = "Pix",
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var body = await ReadJson(await client.GetAsync($"/api/payables/{payableId}"));
        Assert.Equal("Paid", body.GetProperty("status").GetString());
        var inst = body.GetProperty("installments")[0];
        Assert.Equal("Paid", inst.GetProperty("status").GetString());
        Assert.Equal(1, inst.GetProperty("payments").GetArrayLength());
        Assert.Equal(net, inst.GetProperty("payments")[0].GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Register_AboveNet_But_BelowGross_On_Withheld_Payable_Returns_400_Overpay_Not_500()
    {
        // Gross 1000, IRRF 150 → NetAmount = 850.
        // Paying 900 (between net and gross) must return Payment.Overpay (400), not throw (500).
        const decimal gross = 1000m;
        const decimal withheld = 150m;
        const decimal aboveNet = 900m; // > 850 net but < 1000 gross

        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var bankId = await CreateBankAccountAsync(client, seed.CompanyId);
        var (payableId, installmentId) = await CreateWithheldPayableAsync(factory, seed, gross, withheld);

        var res = await client.PostAsJsonAsync($"/api/payables/{payableId}/installments/{installmentId}/payments", new
        {
            paymentDate = "2026-07-10",
            amount = aboveNet,
            bankAccountId = bankId,
            method = "Pix",
        });
        // Must be a clean 400 with Payment.Overpay — not an unhandled 500.
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Payment.Overpay", (await ReadJson(res)).GetProperty("title").GetString());
    }
}
