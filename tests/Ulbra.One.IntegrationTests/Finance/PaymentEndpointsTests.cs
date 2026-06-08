using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

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
        Assert.Equal("Settled", body.GetProperty("status").GetString());
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
        Assert.Equal("Approved", body.GetProperty("status").GetString());
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
        Assert.Equal("Approved", body.GetProperty("status").GetString());
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
}
