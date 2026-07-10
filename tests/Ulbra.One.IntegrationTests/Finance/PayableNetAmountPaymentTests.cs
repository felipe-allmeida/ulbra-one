using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Remittances;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Registry.Domain.Entities;
using Ulbra.One.Registry.Domain.ValueObjects;

namespace Ulbra.One.IntegrationTests.Finance;

/// <summary>
/// Verifies that the remittance path uses <see cref="Installment.NetAmount"/> (net of withholdings)
/// rather than the gross <see cref="Installment.Amount"/>.
///
/// Scenario A — remittance with withholding: <see cref="RemittanceLine.Amount"/> == installment.NetAmount.
/// Regression — no withholdings: remittance path pays Amount exactly (NetAmount == Amount).
///
/// Note: auto-pay (auto-baixa) tests were removed — the system no longer auto-settles installments to Paid.
/// That behavior was intentionally removed; only a human "Dar baixa" or a .RET return import settles.
/// </summary>
[Collection("Postgres")]
public class PayableNetAmountPaymentTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    // ---------------------------------------------------------------------------
    // Seed helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Seeds a Company + BankAccount + Vendor with full TED info.
    /// Returns ids needed for test assertions.
    /// </summary>
    private static async Task<NetAmountSeed> SeedCompanyVendorBankAsync(ApiFactory factory, HttpClient client)
    {
        var cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        var companyRes = await client.PostAsJsonAsync("/api/companies",
            new { legalName = "NetAmount Test Empresa", cnpj });
        companyRes.EnsureSuccessStatusCode();
        var companyPrefixedId = (await ReadJson(companyRes)).GetProperty("id").GetString()!;
        var companyId = Guid.Parse(companyPrefixedId[(companyPrefixedId.LastIndexOf('_') + 1)..]);

        var ccRes = await client.PostAsJsonAsync("/api/cost-centers",
            new { companyId = companyPrefixedId, code = "CC-NET", name = "NetAmount CC", annualBudget = 1_000_000m, autoApprovalLimit = 0m, unitIds = Array.Empty<string>() });
        ccRes.EnsureSuccessStatusCode();
        var costCenterId = (await ReadJson(ccRes)).GetProperty("id").GetString()!;

        // Vendor with full TED bank info.
        var vendorRes = await client.PostAsJsonAsync("/api/vendors", new
        {
            companyId = companyPrefixedId,
            personType = 1,
            name = "Fornecedor Retencao Ltda",
            tradeName = "FornecedorRet",
            document = "11.444.777/0001-61",
            email = "ret@test.com",
            phone = "5133338888",
            address = (object?)null,
            paymentTerms = "30 dias",
            bankInfo = new { bankCode = "341", branch = "0001", account = "654321", accountDigit = "9", pixKey = "" },
        });
        vendorRes.EnsureSuccessStatusCode();
        var vendorPrefixedId = (await ReadJson(vendorRes)).GetProperty("id").GetString()!;
        var vendorId = Guid.Parse(vendorPrefixedId[(vendorPrefixedId.LastIndexOf('_') + 1)..]);

        // Santander bank account with CNAB agreement code + Ted default.
        await using var scope = factory.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var bankAccount = new BankAccount(companyId, "033", "Banco Santander", "5678",
            "00099988", "7", BankAccountType.Checking, "Conta NetAmount", null,
            cnabAgreementCode: "987654321012");
        bankAccount.SetAsDefaultPayer(PaymentMethod.Ted);
        await uow.AddAsync(bankAccount, CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);

        return new NetAmountSeed(companyId, companyPrefixedId, costCenterId, vendorId, vendorPrefixedId, bankAccount.Id);
    }

    /// <summary>
    /// Creates a Scheduled payable with a withholding entirely via domain (no HTTP), so
    /// all mutations happen in one scope and avoid xmin-concurrency conflicts.
    /// Returns the payable Id.
    /// </summary>
    private static async Task<Guid> CreateScheduledPayableWithWithholdingAsync(
        ApiFactory factory, NetAmountSeed seed,
        decimal grossAmount, decimal withholdingAmount)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var now = clock.GetUtcNow();

        var installment = new Installment(1, new DateOnly(2026, 6, 1), grossAmount);

        var payable = new Payable(
            companyId: seed.CompanyId,
            origin: PayableOrigin.Manual,
            vendorId: seed.VendorId,
            counterpartyName: "Fornecedor Retencao Ltda",
            documentNumber: null,
            issueDate: null,
            description: "Payable with IRRF withholding",
            costCenterId: Guid.Parse(seed.CostCenterId[(seed.CostCenterId.LastIndexOf('_') + 1)..]),
            categoryId: null,
            installments: [installment]);

        payable.SetWithholdings([
            (WithholdingTaxType.Irrf, BaseAmount: grossAmount, Rate: null, Amount: withholdingAmount)
        ]);

        // Move to Scheduled so remittance + auto-pay can pick it up. Approval now parks in PendingAccounting;
        // completing the accounting review (NF registered) is what schedules it. The fiscal-doc gate is an
        // endpoint concern, so at the domain level we pass hasFiscalDocument: true.
        payable.Approve("test-user", now);
        payable.CompleteAccounting("test-user", now, hasFiscalDocument: true);

        await uow.AddAsync(payable, CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);

        return payable.Id;
    }

    /// <summary>
    /// Creates a payable via HTTP, approves it (→ Scheduled) — no withholdings.
    /// </summary>
    private static async Task<Guid> CreateScheduledPayableNoWithholdingAsync(
        ApiFactory factory, HttpClient client, NetAmountSeed seed, decimal amount)
    {
        var createRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyPrefixedId,
            vendorId = seed.VendorPrefixedId,
            costCenterId = seed.CostCenterId,
            description = "Payable without withholding",
            installments = new[] { new { dueDate = "2026-06-01", amount } },
            type = "Product", // determined product (no source withholding) — required before scheduling
        });
        createRes.EnsureSuccessStatusCode();
        var payablePrefixedId = (await ReadJson(createRes)).GetProperty("id").GetString()!;
        var payableId = Guid.Parse(payablePrefixedId[(payablePrefixedId.LastIndexOf('_') + 1)..]);

        var approveRes = await client.PostAsJsonAsync($"/api/payables/{payablePrefixedId}/approve", new { });
        approveRes.EnsureSuccessStatusCode();
        await FinanceSeed.CompleteAccountingAsync(factory, client, payablePrefixedId);

        return payableId;
    }

    private sealed record NetAmountSeed(
        Guid CompanyId,
        string CompanyPrefixedId,
        string CostCenterId,
        Guid VendorId,
        string VendorPrefixedId,
        Guid BankAccountId);

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    /// <summary>
    /// (A) Remittance path: when a payable has an IRRF withholding (net < gross),
    /// the generated RemittanceLine.Amount must equal installment.NetAmount (net), not Amount (gross).
    /// </summary>
    [Fact]
    public async Task Remittance_Uses_NetAmount_When_Payable_Has_Withholding()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedCompanyVendorBankAsync(factory, client);

        // Gross 1000, IRRF 150 → NetAmount = 850.
        const decimal gross = 1000m;
        const decimal withheld = 150m;
        const decimal expectedNet = gross - withheld; // 850

        var payableId = await CreateScheduledPayableWithWithholdingAsync(factory, seed, gross, withheld);

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRemittanceGenerationService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var cutoff = new DateOnly(2026, 6, 30);
        var result = await service.GenerateAsync(seed.CompanyId, seed.BankAccountId, cutoff, "test-user", CancellationToken.None);

        Assert.True(result.IsSuccess, $"GenerateAsync failed: {result.Error}");

        // Verify the RemittanceLine.Amount is the net value, NOT the gross.
        var remittance = await uow.ReadSet<Remittance>()
            .FirstOrDefaultAsync(r => r.Id == result.Value.Remittance.Id);
        Assert.NotNull(remittance);
        Assert.Single(remittance!.Lines);

        var line = remittance.Lines.Single(l => l.PayableId == payableId);
        Assert.Equal(expectedNet, line.Amount);
        Assert.NotEqual(gross, line.Amount); // must NOT be the gross amount
    }

    /// <summary>
    /// (Regression A) Remittance path: a payable with NO withholding must still produce a
    /// RemittanceLine.Amount equal to installment.Amount (since NetAmount == Amount).
    /// </summary>
    [Fact]
    public async Task Remittance_Uses_Amount_When_No_Withholding()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedCompanyVendorBankAsync(factory, client);

        const decimal amount = 500m;
        var payableId = await CreateScheduledPayableNoWithholdingAsync(factory, client, seed, amount);

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRemittanceGenerationService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var cutoff = new DateOnly(2026, 6, 30);
        var result = await service.GenerateAsync(seed.CompanyId, seed.BankAccountId, cutoff, "test-user", CancellationToken.None);

        Assert.True(result.IsSuccess, $"GenerateAsync failed: {result.Error}");

        var remittance = await uow.ReadSet<Remittance>()
            .FirstOrDefaultAsync(r => r.Id == result.Value.Remittance.Id);
        Assert.NotNull(remittance);
        Assert.Single(remittance!.Lines);

        var line = remittance.Lines.Single(l => l.PayableId == payableId);
        Assert.Equal(amount, line.Amount); // NetAmount == Amount when no withholdings
    }

}
