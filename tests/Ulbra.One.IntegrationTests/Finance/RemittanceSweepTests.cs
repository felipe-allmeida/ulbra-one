using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Remittances;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.IntegrationTests.Finance;

/// <summary>
/// Integration tests for <see cref="IRemittanceSweepService"/> resolved directly from DI,
/// running against a real Postgres testcontainer.
/// </summary>
[Collection("Postgres")]
public class RemittanceSweepTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private static Guid ParseGuid(string prefixedId) =>
        Guid.Parse(prefixedId[(prefixedId.LastIndexOf('_') + 1)..]);

    // ---------------------------------------------------------------------------
    // Seed helpers
    // ---------------------------------------------------------------------------

    private static async Task<SweepSeedResult> SeedCompanyWithEligibleInstallmentAsync(
        ApiFactory factory,
        HttpClient client,
        DateOnly scheduledPaymentDate,
        bool isHeld = false,
        bool vendorHasBankInfo = true)
    {
        // Company
        var cnpj = FinanceSeed.RandomCnpj();
        var companyRes = await client.PostAsJsonAsync("/api/companies",
            new { legalName = "Empresa Sweep Teste", cnpj });
        companyRes.EnsureSuccessStatusCode();
        var companyPrefixedId = (await ReadJson(companyRes)).GetProperty("id").GetString()!;
        var companyId = ParseGuid(companyPrefixedId);

        // Cost center
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyPrefixedId, "CC-SW", "Sweep TI");

        // Vendor
        object? bankInfo = vendorHasBankInfo
            ? new { bankCode = "", branch = "", account = "", accountDigit = "", pixKey = "pix@sweep-vendor.com" }
            : null;
        var vendorRes = await client.PostAsJsonAsync("/api/vendors", new
        {
            companyId = companyPrefixedId,
            personType = 1,
            name = "Fornecedor Sweep SA",
            tradeName = "SweepVendor",
            document = FinanceSeed.RandomCnpj(),
            email = "sweep@vendor.com",
            phone = "5133330000",
            address = (object?)null,
            paymentTerms = "30 dias",
            bankInfo,
        });
        vendorRes.EnsureSuccessStatusCode();
        var vendorPrefixedId = (await ReadJson(vendorRes)).GetProperty("id").GetString()!;

        // Bank account: Santander 033, default-for-payments, with agreement code
        await using var setupScope = factory.Services.CreateAsyncScope();
        var uow = setupScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var bankAccount = new BankAccount(companyId, "033", "Banco Santander", "1234",
            "00099988", "7", BankAccountType.Checking, "Conta Sweep", null,
            cnabAgreementCode: "SWEEP12345678");
        bankAccount.SetAsDefaultPayer(PaymentMethod.Pix);
        await uow.AddAsync(bankAccount, CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);

        // Payable: due on scheduledPaymentDate, PaymentMethod=Pix
        var payableRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = companyPrefixedId,
            vendorId = vendorPrefixedId,
            costCenterId,
            description = "Payable Sweep",
            installments = new[] { new { dueDate = scheduledPaymentDate.ToString("yyyy-MM-dd"), amount = 800m } },
            type = "Product", // determined product — required before a payable can schedule
            paymentMethod = "Pix",
        });
        payableRes.EnsureSuccessStatusCode();
        var payablePrefixedId = (await ReadJson(payableRes)).GetProperty("id").GetString()!;
        var payableId = ParseGuid(payablePrefixedId);

        // Approve (→ Scheduled)
        var approveRes = await client.PostAsJsonAsync($"/api/payables/{payablePrefixedId}/approve", new { });
        approveRes.EnsureSuccessStatusCode();

        // If held, hold the installment directly via domain
        if (isHeld)
        {
            await using var holdScope = factory.Services.CreateAsyncScope();
            var holdUow = holdScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var payable = await holdUow.Set<Payable>()
                .Include(p => p.Installments)
                .FirstAsync(p => p.Id == payableId);
            foreach (var inst in payable.Installments)
                inst.Hold("held for sweep test");
            await holdUow.SaveChangesAsync(CancellationToken.None);
        }

        return new SweepSeedResult(companyId, bankAccount.Id, payableId);
    }

    private sealed record SweepSeedResult(Guid CompanyId, Guid BankAccountId, Guid PayableId);

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task SweepAsync_HappyPath_GeneratesRemittanceAndMarksInstallmentInRemittance()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        var asOf = new DateOnly(2026, 7, 15);
        var seed = await SeedCompanyWithEligibleInstallmentAsync(factory, client, scheduledPaymentDate: asOf);

        await using var scope = factory.Services.CreateAsyncScope();
        var sweep = scope.ServiceProvider.GetRequiredService<IRemittanceSweepService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var result = await sweep.SweepAsync(asOf, CancellationToken.None);

        Assert.Equal(1, result.RemittancesGenerated);
        Assert.Equal(1, result.InstallmentsIncluded);

        // Remittance persisted with Generated status
        var remittance = await uow.ReadSet<Remittance>()
            .FirstOrDefaultAsync(r => r.PayingBankAccountId == seed.BankAccountId);
        Assert.NotNull(remittance);
        Assert.Equal(RemittanceStatus.Generated, remittance!.Status);
        Assert.Single(remittance.Lines);

        // Installment is InRemittance
        var payable = await uow.ReadSet<Payable>()
            .Include(p => p.Installments)
            .FirstAsync(p => p.Id == seed.PayableId);
        Assert.All(payable.Installments, i => Assert.Equal(InstallmentStatus.InRemittance, i.Status));
    }

    [Fact]
    public async Task SweepAsync_Idempotent_SecondCallProducesNoNewRemittance()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        var asOf = new DateOnly(2026, 7, 15);
        var seed = await SeedCompanyWithEligibleInstallmentAsync(factory, client, scheduledPaymentDate: asOf);

        // First sweep — generates the remittance
        await using var scope1 = factory.Services.CreateAsyncScope();
        var sweep1 = scope1.ServiceProvider.GetRequiredService<IRemittanceSweepService>();
        var result1 = await sweep1.SweepAsync(asOf, CancellationToken.None);
        Assert.Equal(1, result1.RemittancesGenerated);

        // Second sweep — same asOf, installment is now InRemittance (not Open), nothing eligible
        await using var scope2 = factory.Services.CreateAsyncScope();
        var sweep2 = scope2.ServiceProvider.GetRequiredService<IRemittanceSweepService>();
        await sweep2.SweepAsync(asOf, CancellationToken.None);

        // Only one remittance exists for our bank account — idempotent (no double-remit)
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var uow = verifyScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var count = await uow.ReadSet<Remittance>()
            .CountAsync(r => r.PayingBankAccountId == seed.BankAccountId);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SweepAsync_HeldInstallment_NotIncluded_RemainsOpen()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        var asOf = new DateOnly(2026, 7, 15);
        var seed = await SeedCompanyWithEligibleInstallmentAsync(
            factory, client, scheduledPaymentDate: asOf, isHeld: true);

        await using var scope = factory.Services.CreateAsyncScope();
        var sweep = scope.ServiceProvider.GetRequiredService<IRemittanceSweepService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await sweep.SweepAsync(asOf, CancellationToken.None);

        // No remittance for our bank account — held installment is excluded (NothingToRemit for our company)
        var remittanceExists = await uow.ReadSet<Remittance>()
            .AnyAsync(r => r.PayingBankAccountId == seed.BankAccountId);
        Assert.False(remittanceExists);

        // Installment still Open (not InRemittance)
        var payable = await uow.ReadSet<Payable>()
            .Include(p => p.Installments)
            .FirstAsync(p => p.Id == seed.PayableId);
        Assert.All(payable.Installments, i => Assert.Equal(InstallmentStatus.Open, i.Status));
    }

    [Fact]
    public async Task SweepAsync_NonRemittableInstallment_VendorNoBankInfo_NotIncluded_RemainsOpen()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        var asOf = new DateOnly(2026, 7, 15);
        var seed = await SeedCompanyWithEligibleInstallmentAsync(
            factory, client, scheduledPaymentDate: asOf, vendorHasBankInfo: false);

        await using var scope = factory.Services.CreateAsyncScope();
        var sweep = scope.ServiceProvider.GetRequiredService<IRemittanceSweepService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await sweep.SweepAsync(asOf, CancellationToken.None);

        // No remittance was created for our bank account (vendor has no bank info — NothingToRemit)
        var remittanceExists = await uow.ReadSet<Remittance>()
            .AnyAsync(r => r.PayingBankAccountId == seed.BankAccountId);
        Assert.False(remittanceExists);

        // Installment still Open (not InRemittance)
        var payable = await uow.ReadSet<Payable>()
            .Include(p => p.Installments)
            .FirstAsync(p => p.Id == seed.PayableId);
        Assert.All(payable.Installments, i => Assert.Equal(InstallmentStatus.Open, i.Status));
    }
}
