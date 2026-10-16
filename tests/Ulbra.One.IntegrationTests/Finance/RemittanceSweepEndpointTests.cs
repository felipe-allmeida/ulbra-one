using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.IntegrationTests.Finance;

/// <summary>
/// Integration tests for POST /api/remittances/sweep — the manual sweep endpoint.
/// </summary>
[Collection("Postgres")]
public class RemittanceSweepEndpointTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private static Guid ParseGuid(string prefixedId) =>
        Guid.Parse(prefixedId[(prefixedId.LastIndexOf('_') + 1)..]);

    /// <summary>
    /// Seeds a Company + BankAccount (Santander, default payer) + Vendor (with pixKey)
    /// + Payable with one installment scheduled on <paramref name="scheduledPaymentDate"/> → Approved (Scheduled).
    /// </summary>
    private static async Task<SweepEndpointSeed> SeedEligibleInstallmentAsync(
        ApiFactory factory, HttpClient client, DateOnly scheduledPaymentDate)
    {
        // Company
        var cnpj = FinanceSeed.RandomCnpj();
        var companyRes = await client.PostAsJsonAsync("/api/companies",
            new { legalName = "Empresa Endpoint Sweep Teste", cnpj });
        companyRes.EnsureSuccessStatusCode();
        var companyPrefixedId = (await ReadJson(companyRes)).GetProperty("id").GetString()!;
        var companyId = ParseGuid(companyPrefixedId);

        // Cost center
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyPrefixedId, "CC-EP-SW", "Endpoint Sweep TI");

        // Vendor with pixKey
        var vendorRes = await client.PostAsJsonAsync("/api/vendors", new
        {
            companyId = companyPrefixedId,
            personType = 1,
            name = "Fornecedor Endpoint Sweep SA",
            tradeName = "EndpointSweepVendor",
            document = FinanceSeed.RandomCnpj(),
            email = "epsweep@vendor.com",
            phone = "5133330001",
            address = (object?)null,
            paymentTerms = "30 dias",
            bankInfo = new { bankCode = "", branch = "", account = "", accountDigit = "", pixKey = "pix@ep-sweep-vendor.com" },
        });
        vendorRes.EnsureSuccessStatusCode();
        var vendorPrefixedId = (await ReadJson(vendorRes)).GetProperty("id").GetString()!;

        // Bank account: Santander 033, default-for-payments, with CNAB agreement code
        await using var setupScope = factory.Services.CreateAsyncScope();
        var uow = setupScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var bankAccount = new BankAccount(companyId, "033", "Banco Santander", "4321",
            "00077766", "8", BankAccountType.Checking, "Conta Endpoint Sweep", null,
            cnabAgreementCode: "EPSWEEP12345678");
        bankAccount.SetAsDefaultPayer();
        await uow.AddAsync(bankAccount, CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);

        // Payable: due on scheduledPaymentDate, PaymentMethod=Pix
        var payableRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = companyPrefixedId,
            vendorId = vendorPrefixedId,
            costCenterId,
            description = "Payable Endpoint Sweep",
            installments = new[] { new { dueDate = scheduledPaymentDate.ToString("yyyy-MM-dd"), amount = 600m } },
            type = "Product", // determined product — required before a payable can schedule
            paymentMethod = "Pix",
        });
        payableRes.EnsureSuccessStatusCode();
        var payablePrefixedId = (await ReadJson(payableRes)).GetProperty("id").GetString()!;
        var payableId = ParseGuid(payablePrefixedId);

        // Approve (→ PendingAccounting) then complete accounting (NF + confirm) → Scheduled
        var approveRes = await client.PostAsJsonAsync($"/api/payables/{payablePrefixedId}/approve", new { });
        approveRes.EnsureSuccessStatusCode();
        await FinanceSeed.CompleteAccountingAsync(factory, client, payablePrefixedId); // → Scheduled

        return new SweepEndpointSeed(companyId, bankAccount.Id, payableId);
    }

    private sealed record SweepEndpointSeed(Guid CompanyId, Guid BankAccountId, Guid PayableId);

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PostSweep_EligibleInstallment_Returns200_InstallmentMovesToInRemittance()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();

        // Use a unique far-future date to avoid collision with other test data
        var asOf = new DateOnly(2026, 8, 25);
        var seed = await SeedEligibleInstallmentAsync(factory, client, scheduledPaymentDate: asOf);

        // Verify installment is Open before the sweep
        await using var beforeScope = factory.Services.CreateAsyncScope();
        var beforeUow = beforeScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var beforePayable = await beforeUow.ReadSet<Payable>()
            .Include(p => p.Installments)
            .FirstAsync(p => p.Id == seed.PayableId);
        Assert.All(beforePayable.Installments, i => Assert.Equal(InstallmentStatus.Open, i.Status));

        // Act: POST /api/remittances/sweep with the explicit asOf date for determinism
        var response = await client.PostAsJsonAsync("/api/remittances/sweep", new { asOf = asOf.ToString("yyyy-MM-dd") });

        // Assert: 200 OK
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJson(response);
        // At least 1 remittance generated (may be more from other test data on the same asOf)
        Assert.True(body.GetProperty("remittancesGenerated").GetInt32() >= 1);
        Assert.True(body.GetProperty("installmentsIncluded").GetInt32() >= 1);

        // Our specific installment must now be InRemittance
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var uow = verifyScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var payable = await uow.ReadSet<Payable>()
            .Include(p => p.Installments)
            .FirstAsync(p => p.Id == seed.PayableId);
        Assert.All(payable.Installments, i => Assert.Equal(InstallmentStatus.InRemittance, i.Status));

        // Our specific remittance was persisted
        var remittance = await uow.ReadSet<Remittance>()
            .FirstOrDefaultAsync(r => r.PayingBankAccountId == seed.BankAccountId);
        Assert.NotNull(remittance);
        Assert.Equal(RemittanceStatus.Generated, remittance!.Status);
    }

    [Fact]
    public async Task PostSweep_Idempotent_SecondCallForSameAsOf_ProducesNoNewRemittanceForOurAccount()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();

        // Use a unique far-future date
        var asOf = new DateOnly(2026, 8, 26);
        var seed = await SeedEligibleInstallmentAsync(factory, client, scheduledPaymentDate: asOf);

        // First sweep — generates the remittance
        var first = await client.PostAsJsonAsync("/api/remittances/sweep", new { asOf = asOf.ToString("yyyy-MM-dd") });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Second sweep — same asOf; installment is now InRemittance, must not generate a second one for our account
        var second = await client.PostAsJsonAsync("/api/remittances/sweep", new { asOf = asOf.ToString("yyyy-MM-dd") });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        // Verify only one remittance exists for our bank account
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var uow = verifyScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var count = await uow.ReadSet<Remittance>()
            .CountAsync(r => r.PayingBankAccountId == seed.BankAccountId);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task PostSweep_Unauthenticated_Returns401()
    {
        var factory = fixture.Api;
        var unauthenticated = factory.CreateClient();

        var response = await unauthenticated.PostAsJsonAsync("/api/remittances/sweep", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
