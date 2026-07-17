using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Core.Domain.Entities;
using Ulbra.One.Finance.Application.Remittances;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Registry.Domain.Entities;
using Ulbra.One.Registry.Domain.ValueObjects;

namespace Ulbra.One.IntegrationTests.Finance;

/// <summary>
/// Integration tests for <see cref="IRemittanceGenerationService"/> resolved directly from DI,
/// running against a real Postgres testcontainer.
/// </summary>
[Collection("Postgres")]
public class RemittanceGenerationTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    // ---------------------------------------------------------------------------
    // Seed helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Seeds a Company + BankAccount (033, CnabAgreementCode set, DefaultPaymentMethod=Ted),
    /// two vendors (one with full TED BankInfo, one with only PixKey, one with no data),
    /// and three Scheduled payables with due Open installments.
    /// Returns the key ids for assertions.
    /// </summary>
    private static async Task<SeedResult> SeedAsync(
        ApiFactory factory, HttpClient client, bool setBankAgreementCode = true)
    {
        // 1. Company via HTTP (gets auto-seeded by startup).
        var cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        var companyRes = await client.PostAsJsonAsync("/api/companies",
            new { legalName = "Empresa Remessa Test", cnpj });
        companyRes.EnsureSuccessStatusCode();
        var companyPrefixedId = (await ReadJson(companyRes)).GetProperty("id").GetString()!;

        // Parse raw Guid from prefixed id (company_<guid>).
        var companyId = Guid.Parse(companyPrefixedId[(companyPrefixedId.LastIndexOf('_') + 1)..]);

        // 2. Cost center (needed for payable creation).
        var ccRes = await client.PostAsJsonAsync("/api/cost-centers",
            new { companyId = companyPrefixedId, code = "CC-REM", name = "Remessa TI", annualBudget = 1_000_000m, autoApprovalLimit = 0m, unitIds = Array.Empty<string>() });
        ccRes.EnsureSuccessStatusCode();
        var costCenterId = (await ReadJson(ccRes)).GetProperty("id").GetString()!;

        // 3. Vendor A — full TED bank info.
        var vendorARes = await client.PostAsJsonAsync("/api/vendors", new
        {
            companyId = companyPrefixedId,
            personType = 1,
            name = "Fornecedor TED Ltda",
            tradeName = "FornecedorTED",
            document = "11.222.333/0001-81",
            email = "ted@test.com",
            phone = "5133334444",
            address = (object?)null,
            paymentTerms = "30 dias",
            bankInfo = new { bankCode = "341", branch = "0001", account = "123456", accountDigit = "7", pixKey = "" },
        });
        vendorARes.EnsureSuccessStatusCode();
        var vendorAPrefixedId = (await ReadJson(vendorARes)).GetProperty("id").GetString()!;
        var vendorAId = Guid.Parse(vendorAPrefixedId[(vendorAPrefixedId.LastIndexOf('_') + 1)..]);

        // 4. Vendor B — only PixKey.
        var vendorBRes = await client.PostAsJsonAsync("/api/vendors", new
        {
            companyId = companyPrefixedId,
            personType = 1,
            name = "Fornecedor PIX SA",
            tradeName = "FornecedorPIX",
            document = "06.990.590/0001-23",
            email = "pix@test.com",
            phone = "5133335555",
            address = (object?)null,
            paymentTerms = "à vista",
            bankInfo = new { bankCode = "", branch = "", account = "", accountDigit = "", pixKey = "pix@fornecedorpix.com" },
        });
        vendorBRes.EnsureSuccessStatusCode();
        var vendorBPrefixedId = (await ReadJson(vendorBRes)).GetProperty("id").GetString()!;
        var vendorBId = Guid.Parse(vendorBPrefixedId[(vendorBPrefixedId.LastIndexOf('_') + 1)..]);

        // 5. Vendor C — no bank data at all.
        var vendorCRes = await client.PostAsJsonAsync("/api/vendors", new
        {
            companyId = companyPrefixedId,
            personType = 1,
            name = "Fornecedor SemBanco ME",
            tradeName = "SemBanco",
            document = "60.316.817/0001-03",
            email = "nobanco@test.com",
            phone = "5133336666",
            address = (object?)null,
            paymentTerms = "30 dias",
            bankInfo = (object?)null,
        });
        vendorCRes.EnsureSuccessStatusCode();
        var vendorCPrefixedId = (await ReadJson(vendorCRes)).GetProperty("id").GetString()!;
        var vendorCId = Guid.Parse(vendorCPrefixedId[(vendorCPrefixedId.LastIndexOf('_') + 1)..]);

        // 6. Bank account: Santander 033, with or without agreement code, Ted default.
        await using var setupScope = factory.Services.CreateAsyncScope();
        var uow = setupScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var bankAccount = new BankAccount(companyId, "033", "Banco Santander", "1234",
            "00012345", "6", BankAccountType.Checking, "Conta Remessa", null,
            cnabAgreementCode: setBankAgreementCode ? "123456789012" : "");
        bankAccount.SetAsDefaultPayer(PaymentMethod.Ted);
        await uow.AddAsync(bankAccount, CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);

        // 7. Payable A — Vendor A (TED), Scheduled with Open installment due in the past.
        var payableARes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = companyPrefixedId,
            vendorId = vendorAPrefixedId,
            costCenterId,
            description = "Payable TED",
            installments = new[] { new { dueDate = "2026-06-01", amount = 1_000m } },
            type = "Product", // determined product — required before a payable can schedule
        });
        payableARes.EnsureSuccessStatusCode();
        var payableAPrefixedId = (await ReadJson(payableARes)).GetProperty("id").GetString()!;
        var approveARes = await client.PostAsJsonAsync($"/api/payables/{payableAPrefixedId}/approve", new { });
        approveARes.EnsureSuccessStatusCode();
        await FinanceSeed.CompleteAccountingAsync(factory, client, payableAPrefixedId); // → Scheduled
        var payableAId = Guid.Parse(payableAPrefixedId[(payableAPrefixedId.LastIndexOf('_') + 1)..]);

        // 8. Payable B — Vendor B (PIX), Scheduled with Open installment due in the past.
        // Set paymentMethod=Pix so the remittance uses PIX for this vendor (vendor B only has PixKey).
        var payableBRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = companyPrefixedId,
            vendorId = vendorBPrefixedId,
            costCenterId,
            description = "Payable PIX",
            installments = new[] { new { dueDate = "2026-06-10", amount = 500m } },
            type = "Product", // determined product — required before a payable can schedule
            paymentMethod = "Pix",
        });
        payableBRes.EnsureSuccessStatusCode();
        var payableBPrefixedId = (await ReadJson(payableBRes)).GetProperty("id").GetString()!;
        var approveBRes = await client.PostAsJsonAsync($"/api/payables/{payableBPrefixedId}/approve", new { });
        approveBRes.EnsureSuccessStatusCode();
        await FinanceSeed.CompleteAccountingAsync(factory, client, payableBPrefixedId); // → Scheduled
        var payableBId = Guid.Parse(payableBPrefixedId[(payableBPrefixedId.LastIndexOf('_') + 1)..]);

        // 9. Payable C — Vendor C (no bank data), Scheduled. Should be skipped.
        var payableCRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = companyPrefixedId,
            vendorId = vendorCPrefixedId,
            costCenterId,
            description = "Payable SemBanco",
            installments = new[] { new { dueDate = "2026-06-15", amount = 200m } },
            type = "Product", // determined product — required before a payable can schedule
        });
        payableCRes.EnsureSuccessStatusCode();
        var payableCPrefixedId = (await ReadJson(payableCRes)).GetProperty("id").GetString()!;
        var approveCRes = await client.PostAsJsonAsync($"/api/payables/{payableCPrefixedId}/approve", new { });
        approveCRes.EnsureSuccessStatusCode();
        await FinanceSeed.CompleteAccountingAsync(factory, client, payableCPrefixedId); // → Scheduled
        var payableCId = Guid.Parse(payableCPrefixedId[(payableCPrefixedId.LastIndexOf('_') + 1)..]);

        return new SeedResult(companyId, bankAccount.Id, vendorAId, vendorBId, vendorCId,
            payableAId, payableBId, payableCId);
    }

    private sealed record SeedResult(
        Guid CompanyId,
        Guid BankAccountId,
        Guid VendorAId,
        Guid VendorBId,
        Guid VendorCId,
        Guid PayableAId,
        Guid PayableBId,
        Guid PayableCId);

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_Produces_Remittance_With_Two_Lines_And_Skips_NoBankData()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRemittanceGenerationService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();

        var cutoff = new DateOnly(2026, 6, 30);
        var result = await service.GenerateAsync(seed.CompanyId, seed.BankAccountId, cutoff, "test-user", CancellationToken.None);

        Assert.True(result.IsSuccess, $"Expected success but got: {result.Error}");
        var outcome = result.Value;

        // Exactly 2 lines (Vendor A=TED, Vendor B=PIX); Vendor C=skipped.
        Assert.Equal(2, outcome.Remittance.Lines.Count);
        Assert.Single(outcome.Skipped);
        Assert.Equal(seed.PayableCId, outcome.Skipped[0].PayableId);
        Assert.Contains("missing vendor", outcome.Skipped[0].Reason);

        // Remittance persisted.
        var remittance = await uow.ReadSet<Remittance>()
            .FirstOrDefaultAsync(r => r.Id == outcome.Remittance.Id);
        Assert.NotNull(remittance);
        Assert.Equal(2, remittance!.Lines.Count);
        Assert.Equal(RemittanceStatus.Generated, remittance.Status);
        Assert.Equal(1, remittance.SequenceNumber); // first remittance for this account
        Assert.NotNull(remittance.FileStorageKey);

        // File stored.
        var fileStream = await storage.GetAsync(remittance.FileStorageKey!, CancellationToken.None);
        Assert.NotNull(fileStream);

        // Installments for included payables are InRemittance.
        var payableA = await uow.ReadSet<Payable>()
            .Include(p => p.Installments)
            .FirstAsync(p => p.Id == seed.PayableAId);
        Assert.All(payableA.Installments.Where(i => i.DueDate <= cutoff),
            i => Assert.Equal(InstallmentStatus.InRemittance, i.Status));

        var payableB = await uow.ReadSet<Payable>()
            .Include(p => p.Installments)
            .FirstAsync(p => p.Id == seed.PayableBId);
        Assert.All(payableB.Installments.Where(i => i.DueDate <= cutoff),
            i => Assert.Equal(InstallmentStatus.InRemittance, i.Status));

        // Vendor C's installment stays Open.
        var payableC = await uow.ReadSet<Payable>()
            .Include(p => p.Installments)
            .FirstAsync(p => p.Id == seed.PayableCId);
        Assert.All(payableC.Installments, i => Assert.Equal(InstallmentStatus.Open, i.Status));
    }

    [Fact]
    public async Task GenerateAsync_ConcurrentCalls_DoNotCollide_Or_DoubleInclude()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);

        var cutoff = new DateOnly(2026, 6, 30);

        async Task<bool> RunSucceededAsync()
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IRemittanceGenerationService>();
            // The sequence-number race resolves via retry-on-unique-violation — never a thrown 500.
            var result = await service.GenerateAsync(seed.CompanyId, seed.BankAccountId, cutoff, "test-user", CancellationToken.None);
            return result.IsSuccess;
        }

        // Two generations for the same paying account, fired concurrently.
        var results = await Task.WhenAll(Task.Run(RunSucceededAsync), Task.Run(RunSucceededAsync));

        // At least one succeeds; neither throws (no unhandled unique-violation → 500).
        Assert.Contains(true, results);

        await using var scope = factory.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // Exactly one remittance persisted (no duplicate sequence number).
        var remittances = await uow.ReadSet<Remittance>()
            .Where(r => r.PayingBankAccountId == seed.BankAccountId)
            .ToListAsync();
        Assert.Single(remittances);

        // Each eligible installment is InRemittance exactly once — never double-included.
        var payableA = await uow.ReadSet<Payable>().Include(p => p.Installments).FirstAsync(p => p.Id == seed.PayableAId);
        Assert.All(payableA.Installments.Where(i => i.DueDate <= cutoff),
            i => Assert.Equal(InstallmentStatus.InRemittance, i.Status));
    }

    [Fact]
    public async Task CancelAsync_Restores_Installments_To_Open_And_Marks_Cancelled()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);

        // First generate a remittance.
        Guid remittanceId;
        await using (var genScope = factory.Services.CreateAsyncScope())
        {
            var service = genScope.ServiceProvider.GetRequiredService<IRemittanceGenerationService>();
            var cutoff = new DateOnly(2026, 6, 30);
            var result = await service.GenerateAsync(seed.CompanyId, seed.BankAccountId, cutoff, "test-user", CancellationToken.None);
            Assert.True(result.IsSuccess);
            remittanceId = result.Value.Remittance.Id;
        }

        // Cancel in a new scope.
        await using var cancelScope = factory.Services.CreateAsyncScope();
        var cancelService = cancelScope.ServiceProvider.GetRequiredService<IRemittanceGenerationService>();
        var cancelResult = await cancelService.CancelAsync(remittanceId, CancellationToken.None);
        Assert.True(cancelResult.IsSuccess, $"Expected cancel success but got: {cancelResult.Error}");

        // Assert remittance is Cancelled.
        var uow = cancelScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var remittance = await uow.ReadSet<Remittance>()
            .FirstOrDefaultAsync(r => r.Id == remittanceId);
        Assert.NotNull(remittance);
        Assert.Equal(RemittanceStatus.Cancelled, remittance!.Status);

        // Installments for included payables are back to Open.
        var payableA = await uow.ReadSet<Payable>()
            .Include(p => p.Installments)
            .FirstAsync(p => p.Id == seed.PayableAId);
        Assert.All(payableA.Installments, i => Assert.Equal(InstallmentStatus.Open, i.Status));

        var payableB = await uow.ReadSet<Payable>()
            .Include(p => p.Installments)
            .FirstAsync(p => p.Id == seed.PayableBId);
        Assert.All(payableB.Installments, i => Assert.Equal(InstallmentStatus.Open, i.Status));
    }

    [Fact]
    public async Task GenerateAsync_MissingAgreementCode_Returns_Failure_NoRemittance_Installments_Untouched()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client, setBankAgreementCode: false);

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRemittanceGenerationService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var cutoff = new DateOnly(2026, 6, 30);
        var result = await service.GenerateAsync(seed.CompanyId, seed.BankAccountId, cutoff, "test-user", CancellationToken.None);

        // Must fail with a meaningful error.
        Assert.True(result.IsFailure);
        Assert.Contains("agreement", result.Error.Description, StringComparison.OrdinalIgnoreCase);

        // No remittance created for this bank account.
        var exists = await uow.ReadSet<Remittance>()
            .AnyAsync(r => r.PayingBankAccountId == seed.BankAccountId);
        Assert.False(exists);

        // Installments are still Open.
        var payableA = await uow.ReadSet<Payable>()
            .Include(p => p.Installments)
            .FirstAsync(p => p.Id == seed.PayableAId);
        Assert.All(payableA.Installments, i => Assert.Equal(InstallmentStatus.Open, i.Status));
    }

    [Fact]
    public async Task GenerateAsync_NonSantanderBankAccount_Returns_Failure_NoRemittance_Persisted()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        // Minimal seed: just a company. The BankCode guard fires before any payable is loaded,
        // so no payables are needed. Create a non-033 bank account via HTTP to keep it
        // properly scoped and avoid timing conflicts with the EF outbox interceptor.
        var cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        var companyRes = await client.PostAsJsonAsync("/api/companies",
            new { legalName = "Empresa Itaú Test", cnpj });
        companyRes.EnsureSuccessStatusCode();
        var companyPrefixedId = (await ReadJson(companyRes)).GetProperty("id").GetString()!;
        var companyId = Guid.Parse(companyPrefixedId[(companyPrefixedId.LastIndexOf('_') + 1)..]);

        // Create a non-033 bank account via the HTTP API (isDefaultForPayments = false).
        var ledgerAccountId = await FinanceSeed.CreateAccountingAccountAsync(client);
        var bankAccountRes = await client.PostAsJsonAsync("/api/bank-accounts", new
        {
            companyId = companyPrefixedId,
            bankCode = "341",
            bankName = "Banco Itaú",
            agency = "5678",
            accountNumber = "99988877",
            accountDigit = "1",
            accountType = "Checking",
            alias = "Conta Itaú",
            isDefaultForPayments = false,
            ledgerAccountId,
        });
        bankAccountRes.EnsureSuccessStatusCode();
        var bankAccountPrefixedId = (await ReadJson(bankAccountRes)).GetProperty("id").GetString()!;
        var itauAccountId = Guid.Parse(bankAccountPrefixedId[(bankAccountPrefixedId.LastIndexOf('_') + 1)..]);

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRemittanceGenerationService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var cutoff = new DateOnly(2026, 6, 30);
        var result = await service.GenerateAsync(companyId, itauAccountId, cutoff, "test-user", CancellationToken.None);

        // Must fail with bank-not-supported error.
        Assert.True(result.IsFailure);
        Assert.Equal("Remittance.BankNotSupported", result.Error.Code);

        // No remittance created for this bank account.
        var exists = await uow.ReadSet<Remittance>()
            .AnyAsync(r => r.PayingBankAccountId == itauAccountId);
        Assert.False(exists);
    }

    [Fact]
    public async Task GenerateAsync_AllInstallmentsSkipped_Returns_NothingToRemit_NoRemittance_NoSequenceConsumed()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        // Seed a company with a valid Santander account but only Vendor C (no bank data).
        var cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        var companyRes = await client.PostAsJsonAsync("/api/companies",
            new { legalName = "Empresa NothingToRemit", cnpj });
        companyRes.EnsureSuccessStatusCode();
        var companyPrefixedId = (await ReadJson(companyRes)).GetProperty("id").GetString()!;
        var companyId = Guid.Parse(companyPrefixedId[(companyPrefixedId.LastIndexOf('_') + 1)..]);

        var ccRes = await client.PostAsJsonAsync("/api/cost-centers",
            new { companyId = companyPrefixedId, code = "CC-NTR", name = "NothingToRemit TI", annualBudget = 500_000m, autoApprovalLimit = 0m, unitIds = Array.Empty<string>() });
        ccRes.EnsureSuccessStatusCode();
        var costCenterId = (await ReadJson(ccRes)).GetProperty("id").GetString()!;

        // Vendor with NO bank data.
        var vendorRes = await client.PostAsJsonAsync("/api/vendors", new
        {
            companyId = companyPrefixedId,
            personType = 1,
            name = "Fornecedor SemDados SA",
            tradeName = "SemDados",
            document = "33.000.167/0001-01",
            email = "semdados@test.com",
            phone = "5133337777",
            address = (object?)null,
            paymentTerms = "30 dias",
            bankInfo = (object?)null,
        });
        vendorRes.EnsureSuccessStatusCode();
        var vendorPrefixedId = (await ReadJson(vendorRes)).GetProperty("id").GetString()!;

        await using var setupScope = factory.Services.CreateAsyncScope();
        var setupUow = setupScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var bankAccount = new BankAccount(companyId, "033", "Banco Santander", "9876",
            "11122233", "4", BankAccountType.Checking, "Conta Remessa NTR", null,
            cnabAgreementCode: "555555555555");
        bankAccount.SetAsDefaultPayer(PaymentMethod.Ted);
        await setupUow.AddAsync(bankAccount, CancellationToken.None);
        await setupUow.SaveChangesAsync(CancellationToken.None);

        // One payable whose installment will be skipped (no vendor bank data).
        var payableRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = companyPrefixedId,
            vendorId = vendorPrefixedId,
            costCenterId,
            description = "Payable SemDados",
            installments = new[] { new { dueDate = "2026-06-10", amount = 750m } },
            type = "Product", // determined product — required before a payable can schedule
        });
        payableRes.EnsureSuccessStatusCode();
        var payablePrefixedId = (await ReadJson(payableRes)).GetProperty("id").GetString()!;
        var approveRes = await client.PostAsJsonAsync($"/api/payables/{payablePrefixedId}/approve", new { });
        approveRes.EnsureSuccessStatusCode();
        await FinanceSeed.CompleteAccountingAsync(factory, client, payablePrefixedId); // → Scheduled
        var payableId = Guid.Parse(payablePrefixedId[(payablePrefixedId.LastIndexOf('_') + 1)..]);

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRemittanceGenerationService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var cutoff = new DateOnly(2026, 6, 30);
        var result = await service.GenerateAsync(companyId, bankAccount.Id, cutoff, "test-user", CancellationToken.None);

        // Must fail with NothingToRemit.
        Assert.True(result.IsFailure);
        Assert.Equal("Remittance.NothingToRemit", result.Error.Code);

        // No remittance persisted — no sequence consumed.
        var exists = await uow.ReadSet<Remittance>()
            .AnyAsync(r => r.PayingBankAccountId == bankAccount.Id);
        Assert.False(exists);

        // The installment remains Open (not InRemittance).
        var payable = await uow.ReadSet<Payable>()
            .Include(p => p.Installments)
            .FirstAsync(p => p.Id == payableId);
        Assert.All(payable.Installments, i => Assert.Equal(InstallmentStatus.Open, i.Status));
    }
}
