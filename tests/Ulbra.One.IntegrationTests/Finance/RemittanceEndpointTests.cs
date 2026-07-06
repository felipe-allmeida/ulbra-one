using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.IntegrationTests.Finance;

[Collection("Postgres")]
public class RemittanceEndpointTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    // ---------------------------------------------------------------------------
    // Seed helpers (mirrors RemittanceGenerationTests but slimmer — one TED vendor)
    // ---------------------------------------------------------------------------

    private sealed record SeedResult(
        string CompanyPrefixedId,
        Guid CompanyId,
        string BankAccountPrefixedId,
        Guid BankAccountId);

    private static async Task<SeedResult> SeedAsync(ApiFactory factory, HttpClient client)
    {
        // Company
        var cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        var companyRes = await client.PostAsJsonAsync("/api/companies",
            new { legalName = "Empresa Remessa Endpoint Test", cnpj });
        companyRes.EnsureSuccessStatusCode();
        var companyPrefixedId = (await ReadJson(companyRes)).GetProperty("id").GetString()!;
        var companyId = Guid.Parse(companyPrefixedId[(companyPrefixedId.LastIndexOf('_') + 1)..]);

        // Cost center (required for payable)
        var ccRes = await client.PostAsJsonAsync("/api/cost-centers",
            new { companyId = companyPrefixedId, code = "CC-RE", name = "Remessa Endpoint TI", annualBudget = 1_000_000m, autoApprovalLimit = 0m, unitIds = Array.Empty<string>() });
        ccRes.EnsureSuccessStatusCode();
        var costCenterId = (await ReadJson(ccRes)).GetProperty("id").GetString()!;

        // Vendor with full TED bank info
        var vendorRes = await client.PostAsJsonAsync("/api/vendors", new
        {
            companyId = companyPrefixedId,
            personType = 1,
            name = "Fornecedor TED Endpoint Ltda",
            tradeName = "FornecedorTEDEP",
            document = "11.222.333/0001-81",
            email = "tedep@test.com",
            phone = "5133334444",
            address = (object?)null,
            paymentTerms = "30 dias",
            bankInfo = new { bankCode = "341", branch = "0001", account = "123456", accountDigit = "7", pixKey = "" },
        });
        vendorRes.EnsureSuccessStatusCode();
        var vendorPrefixedId = (await ReadJson(vendorRes)).GetProperty("id").GetString()!;

        // Bank account: Santander 033 with agreement code, Ted default
        await using var setupScope = factory.Services.CreateAsyncScope();
        var uow = setupScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var bankAccount = new BankAccount(companyId, "033", "Banco Santander", "1234",
            "00012345", "6", BankAccountType.Checking, "Conta Remessa Endpoint", null,
            cnabAgreementCode: "123456789012");
        bankAccount.SetAsDefaultPayer(PaymentMethod.Ted);
        await uow.AddAsync(bankAccount, CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);

        var bankAccountPrefixedId = $"bank_account_{bankAccount.Id:N}";

        // Payable with due date in the past (will be included in remittance)
        var payableRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = companyPrefixedId,
            vendorId = vendorPrefixedId,
            costCenterId,
            description = "Payable TED Endpoint",
            installments = new[] { new { dueDate = "2026-06-01", amount = 1_000m } },
            type = "Product", // determined product — required before a payable can schedule
        });
        payableRes.EnsureSuccessStatusCode();
        var payablePrefixedId = (await ReadJson(payableRes)).GetProperty("id").GetString()!;
        var approveRes = await client.PostAsJsonAsync($"/api/payables/{payablePrefixedId}/approve", new { });
        approveRes.EnsureSuccessStatusCode();

        return new SeedResult(companyPrefixedId, companyId, bankAccountPrefixedId, bankAccount.Id);
    }

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Anonymous_Request_Returns_401()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var anon = factory.CreateClient();

        var res = await anon.PostAsJsonAsync("/api/remittances", new
        {
            payingBankAccountId = $"bank_account_{Guid.NewGuid():N}",
        });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Create_Get_List_File_Cancel_Happy_Path()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);

        // ── POST /api/remittances → 201 ──────────────────────────────
        var createRes = await client.PostAsJsonAsync("/api/remittances", new
        {
            payingBankAccountId = seed.BankAccountPrefixedId,
            dueCutoff = "2026-06-30",
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);

        var createBody = await ReadJson(createRes);
        var remittanceId = createBody.GetProperty("remittance").GetProperty("id").GetString()!;
        Assert.True(createBody.GetProperty("remittance").GetProperty("lines").GetArrayLength() > 0,
            "Expected at least one line in the remittance");
        // skipped may be empty or have items — just check the field exists
        Assert.True(createBody.TryGetProperty("skipped", out _), "Expected 'skipped' field in response");

        // ── GET /api/remittances (list) → 200 contains it ────────────
        var listRes = await client.GetAsync("/api/remittances");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var listBody = await ReadJson(listRes);
        var items = listBody.GetProperty("items");
        Assert.True(items.GetArrayLength() > 0, "Expected at least one remittance in list");
        Assert.Contains(
            Enumerable.Range(0, items.GetArrayLength()).Select(i => items[i].GetProperty("id").GetString()),
            id => id == remittanceId);

        // ── GET /api/remittances/{id} → 200 with detail ─────────────
        var getRes = await client.GetAsync($"/api/remittances/{remittanceId}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var getBody = await ReadJson(getRes);
        Assert.Equal(remittanceId, getBody.GetProperty("id").GetString());
        Assert.True(getBody.GetProperty("lines").GetArrayLength() > 0, "Expected lines in detail");

        // ── GET /api/remittances/{id}/file → 200, bytes > 0, starts with "033" ──
        var fileRes = await client.GetAsync($"/api/remittances/{remittanceId}/file");
        Assert.Equal(HttpStatusCode.OK, fileRes.StatusCode);
        var fileBytes = await fileRes.Content.ReadAsByteArrayAsync();
        Assert.True(fileBytes.Length > 0, "Expected non-empty file");
        var header = Encoding.ASCII.GetString(fileBytes, 0, Math.Min(3, fileBytes.Length));
        Assert.Equal("033", header);

        // ── POST /api/remittances/{id}/cancel → 200 ─────────────────
        var cancelRes = await client.PostAsync($"/api/remittances/{remittanceId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancelRes.StatusCode);

        // GET after cancel → status = Cancelled
        var afterCancelRes = await client.GetAsync($"/api/remittances/{remittanceId}");
        Assert.Equal(HttpStatusCode.OK, afterCancelRes.StatusCode);
        var afterBody = await ReadJson(afterCancelRes);
        Assert.Equal("Cancelled", afterBody.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Get_NonExistent_Returns_404()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        var res = await client.GetAsync($"/api/remittances/remittance_{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Download_File_Of_NonExistent_Returns_404()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        var res = await client.GetAsync($"/api/remittances/remittance_{Guid.NewGuid():N}/file");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
