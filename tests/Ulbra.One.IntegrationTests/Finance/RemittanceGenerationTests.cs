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
using Ulbra.One.IntegrationTests;
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
        ApiFactory factory, HttpClient client, bool setBankAgreementCode = true,
        string? payerCnpj = null, string? payerLegalName = null)
    {
        // 1. Company via HTTP (gets auto-seeded by startup).
        var cnpj = TestDocuments.RandomCnpj();
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
            document = TestCnpj.New(),
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
            document = TestCnpj.New(),
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
            document = TestCnpj.New(),
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
        bankAccount.SetAsDefaultPayer();
        if (payerCnpj is not null || payerLegalName is not null)
            bankAccount.SetPayerIdentity(payerCnpj, payerLegalName);
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
            paymentMethod = "Ted", // forma na parcela (AEL-933): não há mais default de conta
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
            paymentMethod = "Ted", // forma explícita → cai em "dados bancários ausentes", não "sem forma"
        });
        payableCRes.EnsureSuccessStatusCode();
        var payableCPrefixedId = (await ReadJson(payableCRes)).GetProperty("id").GetString()!;
        var approveCRes = await client.PostAsJsonAsync($"/api/payables/{payableCPrefixedId}/approve", new { });
        approveCRes.EnsureSuccessStatusCode();
        await FinanceSeed.CompleteAccountingAsync(factory, client, payableCPrefixedId); // → Scheduled
        var payableCId = Guid.Parse(payableCPrefixedId[(payableCPrefixedId.LastIndexOf('_') + 1)..]);

        return new SeedResult(companyId, bankAccount.Id, vendorAId, vendorBId, vendorCId,
            payableAId, payableBId, payableCId, companyPrefixedId, costCenterId, vendorAPrefixedId, cnpj);
    }

    private sealed record SeedResult(
        Guid CompanyId,
        Guid BankAccountId,
        Guid VendorAId,
        Guid VendorBId,
        Guid VendorCId,
        Guid PayableAId,
        Guid PayableBId,
        Guid PayableCId,
        string CompanyPrefixedId,
        string CostCenterId,
        string VendorAPrefixedId,
        string CompanyCnpj);

    /// <summary>Resolves the single installment id of a seeded payable straight from the store.</summary>
    private static async Task<Guid> SingleInstallmentIdAsync(ApiFactory factory, Guid payableId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var payable = await uow.ReadSet<Payable>()
            .Include(p => p.Installments)
            .FirstAsync(p => p.Id == payableId);
        return payable.Installments.Single().Id;
    }

    /// <summary>Holds an installment via the public API (pauses it from remittance).</summary>
    private static async Task HoldAsync(HttpClient client, Guid payableId, Guid installmentId, string reason)
    {
        var res = await client.PostAsJsonAsync(
            $"/api/payables/payable_{payableId:N}/installments/installment_{installmentId:N}/hold",
            new { reason });
        res.EnsureSuccessStatusCode();
    }

    private static async Task<InstallmentStatus> InstallmentStatusAsync(ApiFactory factory, Guid payableId, Guid installmentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var payable = await uow.ReadSet<Payable>()
            .Include(p => p.Installments)
            .FirstAsync(p => p.Id == payableId);
        return payable.Installments.Single(i => i.Id == installmentId).Status;
    }

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_Produces_Remittance_With_Two_Lines_And_Skips_NoBankData()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRemittanceGenerationService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();

        var cutoff = new DateOnly(2026, 6, 30);
        var result = await service.GenerateAsync(seed.CompanyId, seed.BankAccountId, cutoff, "test-user", null, null, CancellationToken.None);

        Assert.True(result.IsSuccess, $"Expected success but got: {result.Error}");
        var outcome = result.Value;

        // Two files now (AEL-933): one per forma — Vendor A=TED, Vendor B=PIX. Vendor C=skipped.
        Assert.Equal(2, outcome.Remittances.Count);
        Assert.All(outcome.Remittances, r => Assert.Single(r.Lines)); // one line each
        Assert.Equal(
            new[] { RemittanceCategory.Pix, RemittanceCategory.Ted },
            outcome.Remittances.Select(r => r.Category!.Value).OrderBy(c => c).ToArray());
        Assert.Single(outcome.Skipped);
        Assert.Equal(seed.PayableCId, outcome.Skipped[0].PayableId);
        Assert.Contains("missing vendor", outcome.Skipped[0].Reason);

        // Both remittances persisted with distinct NSAs (1 and 2) and a stored file each.
        var persisted = await uow.ReadSet<Remittance>()
            .Where(r => r.PayingBankAccountId == seed.BankAccountId)
            .ToListAsync();
        Assert.Equal(2, persisted.Count);
        Assert.All(persisted, r => Assert.Single(r.Lines));
        Assert.Equal(new[] { 1, 2 }, persisted.Select(r => r.SequenceNumber).OrderBy(n => n).ToArray());
        Assert.All(persisted, r => Assert.Equal(RemittanceStatus.Generated, r.Status));
        foreach (var r in persisted)
        {
            Assert.NotNull(r.FileStorageKey);
            Assert.NotNull(await storage.GetAsync(r.FileStorageKey!, CancellationToken.None));
        }

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
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);

        var cutoff = new DateOnly(2026, 6, 30);

        async Task<bool> RunSucceededAsync()
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IRemittanceGenerationService>();
            // The sequence-number race resolves via retry-on-unique-violation — never a thrown 500.
            var result = await service.GenerateAsync(seed.CompanyId, seed.BankAccountId, cutoff, "test-user", null, null, CancellationToken.None);
            return result.IsSuccess;
        }

        // Two generations for the same paying account, fired concurrently.
        var results = await Task.WhenAll(Task.Run(RunSucceededAsync), Task.Run(RunSucceededAsync));

        // At least one succeeds; neither throws (no unhandled unique-violation → 500).
        Assert.Contains(true, results);

        await using var scope = factory.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // Exactly the winning run's files persist — one per forma (PIX + TED = 2), with distinct NSAs and no
        // double-include. The loser collides on the sequence number, retries, finds nothing eligible and fails.
        var remittances = await uow.ReadSet<Remittance>()
            .Where(r => r.PayingBankAccountId == seed.BankAccountId)
            .ToListAsync();
        Assert.Equal(2, remittances.Count);
        Assert.Equal(2, remittances.Select(r => r.SequenceNumber).Distinct().Count());

        // Each eligible installment is InRemittance exactly once — never double-included.
        var payableA = await uow.ReadSet<Payable>().Include(p => p.Installments).FirstAsync(p => p.Id == seed.PayableAId);
        Assert.All(payableA.Installments.Where(i => i.DueDate <= cutoff),
            i => Assert.Equal(InstallmentStatus.InRemittance, i.Status));
    }

    [Fact]
    public async Task CancelAsync_Restores_Installments_To_Open_And_Marks_Cancelled()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);

        // First generate the remittances (one per forma — PIX + TED).
        List<Guid> remittanceIds;
        await using (var genScope = factory.Services.CreateAsyncScope())
        {
            var service = genScope.ServiceProvider.GetRequiredService<IRemittanceGenerationService>();
            var cutoff = new DateOnly(2026, 6, 30);
            var result = await service.GenerateAsync(seed.CompanyId, seed.BankAccountId, cutoff, "test-user", null, null, CancellationToken.None);
            Assert.True(result.IsSuccess);
            remittanceIds = result.Value.Remittances.Select(r => r.Id).ToList();
            Assert.Equal(2, remittanceIds.Count);
        }

        // Cancel each in a new scope.
        await using var cancelScope = factory.Services.CreateAsyncScope();
        var cancelService = cancelScope.ServiceProvider.GetRequiredService<IRemittanceGenerationService>();
        foreach (var remittanceId in remittanceIds)
        {
            var cancelResult = await cancelService.CancelAsync(remittanceId, CancellationToken.None);
            Assert.True(cancelResult.IsSuccess, $"Expected cancel success but got: {cancelResult.Error}");
        }

        // Assert every remittance is Cancelled.
        var uow = cancelScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        foreach (var remittanceId in remittanceIds)
        {
            var remittance = await uow.ReadSet<Remittance>().FirstOrDefaultAsync(r => r.Id == remittanceId);
            Assert.NotNull(remittance);
            Assert.Equal(RemittanceStatus.Cancelled, remittance!.Status);
        }

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
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client, setBankAgreementCode: false);

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRemittanceGenerationService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var cutoff = new DateOnly(2026, 6, 30);
        var result = await service.GenerateAsync(seed.CompanyId, seed.BankAccountId, cutoff, "test-user", null, null, CancellationToken.None);

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
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();

        // Minimal seed: just a company. The BankCode guard fires before any payable is loaded,
        // so no payables are needed. Create a non-033 bank account via HTTP to keep it
        // properly scoped and avoid timing conflicts with the EF outbox interceptor.
        var cnpj = TestDocuments.RandomCnpj();
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
        var result = await service.GenerateAsync(companyId, itauAccountId, cutoff, "test-user", null, null, CancellationToken.None);

        // Must fail with bank-not-supported error.
        Assert.True(result.IsFailure);
        Assert.Equal("remittance.bank_not_supported", result.Error.Code);

        // No remittance created for this bank account.
        var exists = await uow.ReadSet<Remittance>()
            .AnyAsync(r => r.PayingBankAccountId == itauAccountId);
        Assert.False(exists);
    }

    [Fact]
    public async Task GenerateAsync_AllInstallmentsSkipped_Returns_NothingToRemit_NoRemittance_NoSequenceConsumed()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();

        // Seed a company with a valid Santander account but only Vendor C (no bank data).
        var cnpj = TestDocuments.RandomCnpj();
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
            document = TestCnpj.New(),
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
        bankAccount.SetAsDefaultPayer();
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
            paymentMethod = "Ted", // forma explícita → skip por "dados bancários ausentes" (AEL-933)
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
        var result = await service.GenerateAsync(companyId, bankAccount.Id, cutoff, "test-user", null, null, CancellationToken.None);

        // Must fail with NothingToRemit, naming the reason (pt-BR) so the operator can act.
        Assert.True(result.IsFailure);
        Assert.Equal("remittance.nothing_to_remit", result.Error.Code);
        Assert.Equal("1 parcela descartada: 1× dados bancários do fornecedor ausentes.", result.Error.Description);

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

    // ---------------------------------------------------------------------------
    // Selection mode (installmentIds)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_SelectionSubset_Includes_Only_Selected_Installments()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);
        var installmentA = await SingleInstallmentIdAsync(factory, seed.PayableAId);
        var installmentB = await SingleInstallmentIdAsync(factory, seed.PayableBId);

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRemittanceGenerationService>();

        var result = await service.GenerateAsync(seed.CompanyId, seed.BankAccountId,
            new DateOnly(2026, 6, 30), "test-user", [installmentA], null, CancellationToken.None);

        Assert.True(result.IsSuccess, $"Expected success but got: {result.Error}");
        var line = Assert.Single(Assert.Single(result.Value.Remittances).Lines);
        Assert.Equal(installmentA, line.InstallmentId);
        Assert.Empty(result.Value.Skipped);

        // Only the selected installment moved; the eligible-but-unselected one stays Open.
        Assert.Equal(InstallmentStatus.InRemittance, await InstallmentStatusAsync(factory, seed.PayableAId, installmentA));
        Assert.Equal(InstallmentStatus.Open, await InstallmentStatusAsync(factory, seed.PayableBId, installmentB));
    }

    [Fact]
    public async Task GenerateAsync_Selected_Held_Installment_Is_Skipped_As_Held()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);
        var installmentA = await SingleInstallmentIdAsync(factory, seed.PayableAId);
        var installmentB = await SingleInstallmentIdAsync(factory, seed.PayableBId);
        await HoldAsync(client, seed.PayableAId, installmentA, "Aguardando validação");

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRemittanceGenerationService>();

        var result = await service.GenerateAsync(seed.CompanyId, seed.BankAccountId,
            new DateOnly(2026, 6, 30), "test-user", [installmentA, installmentB], null, CancellationToken.None);

        Assert.True(result.IsSuccess, $"Expected success but got: {result.Error}");
        var line = Assert.Single(Assert.Single(result.Value.Remittances).Lines);
        Assert.Equal(installmentB, line.InstallmentId);

        var skip = Assert.Single(result.Value.Skipped);
        Assert.Equal(seed.PayableAId, skip.PayableId);
        Assert.Equal(installmentA, skip.InstallmentId);
        Assert.Equal("installment is held", skip.Reason);
        Assert.Equal(InstallmentStatus.Open, await InstallmentStatusAsync(factory, seed.PayableAId, installmentA));
    }

    [Fact]
    public async Task GenerateAsync_Selected_NotOpen_Installment_Is_Skipped_As_NotOpen()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);
        var installmentA = await SingleInstallmentIdAsync(factory, seed.PayableAId);
        var installmentB = await SingleInstallmentIdAsync(factory, seed.PayableBId);

        // First selection remits A → its installment becomes InRemittance.
        await using (var firstScope = factory.Services.CreateAsyncScope())
        {
            var firstService = firstScope.ServiceProvider.GetRequiredService<IRemittanceGenerationService>();
            var first = await firstService.GenerateAsync(seed.CompanyId, seed.BankAccountId,
                new DateOnly(2026, 6, 30), "test-user", [installmentA], null, CancellationToken.None);
            Assert.True(first.IsSuccess, $"Expected success but got: {first.Error}");
        }

        // Second selection re-checks A (now InRemittance) alongside eligible B.
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRemittanceGenerationService>();
        var result = await service.GenerateAsync(seed.CompanyId, seed.BankAccountId,
            new DateOnly(2026, 6, 30), "test-user", [installmentA, installmentB], null, CancellationToken.None);

        Assert.True(result.IsSuccess, $"Expected success but got: {result.Error}");
        var line = Assert.Single(Assert.Single(result.Value.Remittances).Lines);
        Assert.Equal(installmentB, line.InstallmentId);

        var skip = Assert.Single(result.Value.Skipped);
        Assert.Equal(seed.PayableAId, skip.PayableId);
        Assert.Equal(installmentA, skip.InstallmentId);
        Assert.Equal("installment is not open", skip.Reason);
    }

    [Fact]
    public async Task GenerateAsync_Selected_Unknown_Id_Is_Skipped_As_NotFound_With_Empty_PayableId()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);
        var installmentB = await SingleInstallmentIdAsync(factory, seed.PayableBId);
        var unknownId = Guid.NewGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRemittanceGenerationService>();

        var result = await service.GenerateAsync(seed.CompanyId, seed.BankAccountId,
            new DateOnly(2026, 6, 30), "test-user", [installmentB, unknownId], null, CancellationToken.None);

        Assert.True(result.IsSuccess, $"Expected success but got: {result.Error}");
        var line = Assert.Single(Assert.Single(result.Value.Remittances).Lines);
        Assert.Equal(installmentB, line.InstallmentId);

        var skip = Assert.Single(result.Value.Skipped);
        Assert.Equal(Guid.Empty, skip.PayableId);
        Assert.Equal(unknownId, skip.InstallmentId);
        Assert.Equal("installment not found among Scheduled payables of this company", skip.Reason);
    }

    [Fact]
    public async Task GenerateAsync_Selected_FutureDated_Installment_Rides_Despite_Cutoff()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);

        // Payable due far in the future (vendor A has full TED bank info) — Scheduled.
        var futureDue = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
        var payableRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyPrefixedId,
            vendorId = seed.VendorAPrefixedId,
            costCenterId = seed.CostCenterId,
            description = "Payable futuro selecionado",
            installments = new[] { new { dueDate = futureDue.ToString("yyyy-MM-dd"), amount = 777m } },
            type = "Product", // determined product — required before a payable can schedule
            paymentMethod = "Ted", // forma na parcela (AEL-933)
        });
        payableRes.EnsureSuccessStatusCode();
        var payablePrefixedId = (await ReadJson(payableRes)).GetProperty("id").GetString()!;
        (await client.PostAsJsonAsync($"/api/payables/{payablePrefixedId}/approve", new { })).EnsureSuccessStatusCode();
        await FinanceSeed.CompleteAccountingAsync(factory, client, payablePrefixedId); // → Scheduled
        var payableId = Guid.Parse(payablePrefixedId[(payablePrefixedId.LastIndexOf('_') + 1)..]);
        var futureInstallment = await SingleInstallmentIdAsync(factory, payableId);

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRemittanceGenerationService>();

        // Cutoff far BEFORE the due date — in selection mode it must be ignored.
        var result = await service.GenerateAsync(seed.CompanyId, seed.BankAccountId,
            new DateOnly(2026, 6, 30), "test-user", [futureInstallment], null, CancellationToken.None);

        Assert.True(result.IsSuccess, $"Expected success but got: {result.Error}");
        var line = Assert.Single(Assert.Single(result.Value.Remittances).Lines);
        Assert.Equal(futureInstallment, line.InstallmentId);
        Assert.Equal(777m, line.Amount);
        Assert.Equal(InstallmentStatus.InRemittance, await InstallmentStatusAsync(factory, payableId, futureInstallment));
    }

    [Fact]
    public async Task GenerateAsync_Selection_With_All_Ineligible_Returns_NothingToRemit()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);
        var installmentA = await SingleInstallmentIdAsync(factory, seed.PayableAId);
        await HoldAsync(client, seed.PayableAId, installmentA, "Bloqueio total");

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRemittanceGenerationService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var result = await service.GenerateAsync(seed.CompanyId, seed.BankAccountId,
            new DateOnly(2026, 6, 30), "test-user", [installmentA], null, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("remittance.nothing_to_remit", result.Error.Code);
        Assert.Equal("1 parcela descartada: 1× parcela em espera.", result.Error.Description);

        // No remittance persisted and the held installment stays Open.
        var exists = await uow.ReadSet<Remittance>()
            .AnyAsync(r => r.PayingBankAccountId == seed.BankAccountId);
        Assert.False(exists);
        Assert.Equal(InstallmentStatus.Open, await InstallmentStatusAsync(factory, seed.PayableAId, installmentA));
    }

    [Fact]
    public async Task GenerateAsync_Nothing_Due_Before_Cutoff_Says_Nothing_Was_Due()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client); // due dates 2026-06-01 / 06-10 / 06-15

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRemittanceGenerationService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // Cutoff earlier than every due date → nothing due, nothing skipped.
        var result = await service.GenerateAsync(seed.CompanyId, seed.BankAccountId,
            new DateOnly(2026, 5, 1), "test-user", null, null, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("remittance.nothing_to_remit", result.Error.Code);
        Assert.Equal("Nenhuma parcela vencia até 01/05/2026.", result.Error.Description);

        var exists = await uow.ReadSet<Remittance>()
            .AnyAsync(r => r.PayingBankAccountId == seed.BankAccountId);
        Assert.False(exists);
    }

    [Fact]
    public async Task GenerateAsync_Account_With_TwoChar_AgencyDigit_Fails_Diagnosably_Not_500()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);

        // Reproduce the incident: BankAccount.AgencyDigit is varchar(2) but the CNAB header dv_agencia
        // field is a single char (non-truncating). A stored 2-char value used to overflow the writer as
        // an unhandled 500; it must now surface as a diagnosable Result error (→ 422) with no throw.
        await using (var profileScope = factory.Services.CreateAsyncScope())
        {
            var uow = profileScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var account = await uow.Set<BankAccount>().FirstAsync(a => a.Id == seed.BankAccountId);
            account.SetRemittanceProfile("12", "", "", "", "", "", "");
            await uow.SaveChangesAsync(CancellationToken.None);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRemittanceGenerationService>();
        var uow2 = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var result = await service.GenerateAsync(seed.CompanyId, seed.BankAccountId,
            new DateOnly(2026, 6, 30), "test-user", null, null, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("remittance.invalid_payer_data", result.Error.Code);

        // No remittance persisted and no sequence consumed — the bad payer blocks the whole run.
        var exists = await uow2.ReadSet<Remittance>().AnyAsync(r => r.PayingBankAccountId == seed.BankAccountId);
        Assert.False(exists);
    }

    // ---------------------------------------------------------------------------
    // Payer identity override (BankAccount.PayerCnpj / PayerLegalName) — AEL-910
    // ---------------------------------------------------------------------------

    /// <summary>Reads the stored CNAB file of a remittance and splits it into 240-char records.</summary>
    private static async Task<string[]> ReadFileRecordsAsync(ApiFactory factory, Guid remittanceId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
        var remittance = await uow.ReadSet<Remittance>().FirstAsync(r => r.Id == remittanceId);
        await using var stream = await storage.GetAsync(remittance.FileStorageKey!, CancellationToken.None);
        Assert.NotNull(stream);
        using var ms = new MemoryStream();
        await stream!.CopyToAsync(ms);
        var text = System.Text.Encoding.Latin1.GetString(ms.ToArray());
        return text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>1-based field slice per docs/cnab240-layout.md.</summary>
    private static string Field(string record, int start, int length) => record.Substring(start - 1, length);

    [Fact]
    public async Task GenerateAsync_Account_PayerCnpj_Overrides_Company_In_Header_And_Lote()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        // The account pays under a different establishment CNPJ than its owning company (ULBRA case).
        const string overrideCnpj = "88332580009111";
        var seed = await SeedAsync(factory, client, payerCnpj: overrideCnpj, payerLegalName: "ULBRA ESTABELECIMENTO");
        Assert.NotEqual(overrideCnpj, seed.CompanyCnpj); // random company cnpj differs from the override

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRemittanceGenerationService>();
        var result = await service.GenerateAsync(seed.CompanyId, seed.BankAccountId,
            new DateOnly(2026, 6, 30), "test-user", null, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Expected success but got: {result.Error}");

        // Payer header is account-level (identical across the per-forma files); read the first file.
        var records = await ReadFileRecordsAsync(factory, result.Value.Remittances[0].Id);
        // Header de Arquivo (record 0) + Header de Lote (record 1) both carry the override CNPJ at pos 19-32.
        Assert.Equal(overrideCnpj, Field(records[0], 19, 14));
        Assert.Equal(overrideCnpj, Field(records[1], 19, 14));
        // nome_empresa (pos 73-102): override legal name, uppercased and padded.
        Assert.Equal("ULBRA ESTABELECIMENTO".PadRight(30), Field(records[0], 73, 30));
        // The owning company's CNPJ must NOT appear as the payer.
        Assert.NotEqual(seed.CompanyCnpj, Field(records[0], 19, 14));
    }

    [Fact]
    public async Task GenerateAsync_FallsBackToCompany_Cnpj_When_Account_Has_No_Override()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client); // no payer override configured

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRemittanceGenerationService>();
        var result = await service.GenerateAsync(seed.CompanyId, seed.BankAccountId,
            new DateOnly(2026, 6, 30), "test-user", null, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Expected success but got: {result.Error}");

        // Payer header is account-level (identical across the per-forma files); read the first file.
        var records = await ReadFileRecordsAsync(factory, result.Value.Remittances[0].Id);
        // Both the file header and the lote header fall back to the owning company's CNPJ.
        Assert.Equal(seed.CompanyCnpj, Field(records[0], 19, 14));
        Assert.Equal(seed.CompanyCnpj, Field(records[1], 19, 14));
    }

    [Fact]
    public async Task GenerateAsync_names_the_file_by_forma_nsa_and_datetime()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRemittanceGenerationService>();
        var result = await service.GenerateAsync(seed.CompanyId, seed.BankAccountId,
            new DateOnly(2026, 6, 30), "test-user", null, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Expected success but got: {result.Error}");

        // Name carries the forma + NSA so several files from one run never collide on the timestamp
        // (AEL-933): SANTANDER_REMESSA_<FORMA>_<NSA 6d>_<yyyyMMddHHmmss>.txt.
        Assert.All(result.Value.Remittances, r =>
            Assert.Matches(@"^SANTANDER_REMESSA_(PIX|TED|BOLETO|CONVENIO)_\d{6}_\d{14}\.txt$", r.FileName!));
    }

    // ---------------------------------------------------------------------------
    // Data de pagamento (data_pagamento) — agendamento para data futura
    // ---------------------------------------------------------------------------

    /// <summary>Segmento A detail records ("3" at pos 8, "A" at pos 14) of a CNAB file.</summary>
    private static List<string> SegmentARecords(string[] records) =>
        records.Where(r => Field(r, 8, 1) == "3" && Field(r, 14, 1) == "A").ToList();

    [Fact]
    public async Task GenerateAsync_Stamps_The_Requested_Future_PaymentDate_In_Segment_A()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRemittanceGenerationService>();

        // The operator schedules the batch for a date the bank should execute later — every line of
        // every per-forma file must carry that date, not the generation date.
        var paymentDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10);
        var result = await service.GenerateAsync(seed.CompanyId, seed.BankAccountId,
            new DateOnly(2026, 6, 30), "test-user", null, paymentDate, CancellationToken.None);

        Assert.True(result.IsSuccess, $"Expected success but got: {result.Error}");

        foreach (var remittance in result.Value.Remittances)
        {
            var segments = SegmentARecords(await ReadFileRecordsAsync(factory, remittance.Id));
            Assert.NotEmpty(segments);
            Assert.All(segments, rec => Assert.Equal(paymentDate.ToString("ddMMyyyy"), Field(rec, 94, 8)));
            // Persisted too: without it nobody can tell after the fact which date the file was sent for.
            Assert.Equal(paymentDate, remittance.PaymentDate);
        }
    }

    [Fact]
    public async Task GenerateAsync_Defaults_The_PaymentDate_To_Today_When_None_Is_Requested()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(factory, client);

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRemittanceGenerationService>();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await service.GenerateAsync(seed.CompanyId, seed.BankAccountId,
            new DateOnly(2026, 6, 30), "test-user", null, null, CancellationToken.None);

        Assert.True(result.IsSuccess, $"Expected success but got: {result.Error}");

        foreach (var remittance in result.Value.Remittances)
        {
            var segments = SegmentARecords(await ReadFileRecordsAsync(factory, remittance.Id));
            Assert.NotEmpty(segments);
            Assert.All(segments, rec => Assert.Equal(today.ToString("ddMMyyyy"), Field(rec, 94, 8)));
            Assert.Equal(today, remittance.PaymentDate);
        }
    }
}
