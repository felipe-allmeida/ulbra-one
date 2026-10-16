using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.IntegrationTests;

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
        Guid BankAccountId,
        string VendorPrefixedId,
        string CostCenterId,
        string PayablePrefixedId,
        string InstallmentPrefixedId);

    private static async Task<SeedResult> SeedAsync(ApiFactory factory, HttpClient client)
    {
        // Company
        var cnpj = TestDocuments.RandomCnpj();
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
            document = TestCnpj.New(),
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
        bankAccount.SetAsDefaultPayer();
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
            paymentMethod = "Ted", // forma na parcela (AEL-933)
        });
        payableRes.EnsureSuccessStatusCode();
        var payablePrefixedId = (await ReadJson(payableRes)).GetProperty("id").GetString()!;
        var approveRes = await client.PostAsJsonAsync($"/api/payables/{payablePrefixedId}/approve", new { });
        approveRes.EnsureSuccessStatusCode();
        await FinanceSeed.CompleteAccountingAsync(factory, client, payablePrefixedId); // → Scheduled

        var payableBody = await ReadJson(await client.GetAsync($"/api/payables/{payablePrefixedId}"));
        var installmentPrefixedId = payableBody.GetProperty("installments")[0].GetProperty("id").GetString()!;

        return new SeedResult(companyPrefixedId, companyId, bankAccountPrefixedId, bankAccount.Id,
            vendorPrefixedId, costCenterId, payablePrefixedId, installmentPrefixedId);
    }

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Anonymous_Request_Returns_401()
    {
        var factory = fixture.Api;
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
        var factory = fixture.Api;
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
        var createdRemittances = createBody.GetProperty("remittances");
        Assert.True(createdRemittances.GetArrayLength() >= 1, "Expected at least one remittance");
        var remittanceId = createdRemittances[0].GetProperty("id").GetString()!;
        Assert.True(createdRemittances[0].GetProperty("lines").GetArrayLength() > 0,
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
    public async Task Create_With_InstallmentIds_Returns_201_With_Only_The_Selected_Lines()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);

        // Second eligible Scheduled payable — must stay out of the remittance (not selected).
        var otherRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyPrefixedId,
            vendorId = seed.VendorPrefixedId,
            costCenterId = seed.CostCenterId,
            description = "Payable fora da seleção",
            installments = new[] { new { dueDate = "2026-06-05", amount = 750m } },
            type = "Product", // determined product — required before a payable can schedule
        });
        otherRes.EnsureSuccessStatusCode();
        var otherPayableId = (await ReadJson(otherRes)).GetProperty("id").GetString()!;
        (await client.PostAsJsonAsync($"/api/payables/{otherPayableId}/approve", new { })).EnsureSuccessStatusCode();
        await FinanceSeed.CompleteAccountingAsync(factory, client, otherPayableId); // → Scheduled

        var createRes = await client.PostAsJsonAsync("/api/remittances", new
        {
            payingBankAccountId = seed.BankAccountPrefixedId,
            installmentIds = new[] { seed.InstallmentPrefixedId },
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);

        var body = await ReadJson(createRes);
        var lines = body.GetProperty("remittances")[0].GetProperty("lines");
        Assert.Equal(1, lines.GetArrayLength());
        var selectedGuid = Guid.Parse(seed.InstallmentPrefixedId[(seed.InstallmentPrefixedId.LastIndexOf('_') + 1)..]);
        Assert.Equal(selectedGuid, lines[0].GetProperty("installmentId").GetGuid());
        Assert.Equal(0, body.GetProperty("skipped").GetArrayLength());

        // The unselected payable's installment is untouched.
        var otherBody = await ReadJson(await client.GetAsync($"/api/payables/{otherPayableId}"));
        Assert.Equal("Open", otherBody.GetProperty("installments")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Get_NonExistent_Returns_404()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();

        var res = await client.GetAsync($"/api/remittances/remittance_{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Download_File_Of_NonExistent_Returns_404()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();

        var res = await client.GetAsync($"/api/remittances/remittance_{Guid.NewGuid():N}/file");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    // ---------------------------------------------------------------------------
    // Data de pagamento (agendamento para data futura)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Create_With_Future_PaymentDate_Returns_201_And_Echoes_The_Date()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);

        var paymentDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);
        var res = await client.PostAsJsonAsync("/api/remittances", new
        {
            payingBankAccountId = seed.BankAccountPrefixedId,
            dueCutoff = "2026-06-30",
            paymentDate = paymentDate.ToString("yyyy-MM-dd"),
        });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var remittances = (await ReadJson(res)).GetProperty("remittances");
        Assert.True(remittances.GetArrayLength() >= 1, "Expected at least one remittance");
        for (var i = 0; i < remittances.GetArrayLength(); i++)
            Assert.Equal(paymentDate.ToString("yyyy-MM-dd"), remittances[i].GetProperty("paymentDate").GetString());
    }

    [Fact]
    public async Task Create_With_PaymentDate_In_The_Past_Returns_400()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);

        // The bank rejects a file dated before the day it is delivered — refuse it here, diagnosably,
        // instead of letting the operator find out at the bank.
        var res = await client.PostAsJsonAsync("/api/remittances", new
        {
            payingBankAccountId = seed.BankAccountPrefixedId,
            dueCutoff = "2026-06-30",
            paymentDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1).ToString("yyyy-MM-dd"),
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(ErrorCodes.Remittance.PaymentDateInPast,
            (await ReadJson(res)).GetProperty("error_code").GetString());
    }
}
