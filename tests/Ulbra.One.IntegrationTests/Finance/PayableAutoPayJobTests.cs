using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain;
using Ulbra.One.Finance.Application.Payments;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Finance.Infrastructure.Jobs;

namespace Ulbra.One.IntegrationTests.Finance;

/// <summary>
/// Integration tests for PayableAutoPayJob.RunOnceAsync — invoked directly (no timer) against
/// a real Postgres testcontainer. AutoPay:Enabled=false in ApiFactory prevents the background
/// timer from interfering with the rest of the suite.
/// </summary>
[Collection("Postgres")]
public class PayableAutoPayJobTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private sealed record Seed(string CompanyId, string CostCenterId, string VendorId);

    // ---------------------------------------------------------------------------
    // Seed helpers
    // ---------------------------------------------------------------------------

    private static async Task<Seed> SeedAsync(HttpClient client)
    {
        var cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        var companyRes = await client.PostAsJsonAsync("/api/companies",
            new { legalName = "ULBRA AutoPay Test", cnpj });
        companyRes.EnsureSuccessStatusCode();
        var companyId = (await ReadJson(companyRes)).GetProperty("id").GetString()!;

        var ccRes = await client.PostAsJsonAsync("/api/cost-centers",
            new { companyId, code = "CC1", name = "TI", annualBudget = 500_000m, autoApprovalLimit = 0m, unitIds = Array.Empty<string>() });
        ccRes.EnsureSuccessStatusCode();
        var costCenterId = (await ReadJson(ccRes)).GetProperty("id").GetString()!;

        var vendorRes = await client.PostAsJsonAsync("/api/vendors", new
        {
            companyId,
            personType = 1,
            name = "Fornecedor AutoPay",
            tradeName = "AutoPay Ltda",
            document = "11.222.333/0001-81",
            email = "autopay@test.com",
            phone = "5133334444",
            address = (object?)null,
            paymentTerms = "30 dias",
            bankInfo = (string?)null,
        });
        vendorRes.EnsureSuccessStatusCode();
        var vendorId = (await ReadJson(vendorRes)).GetProperty("id").GetString()!;

        return new Seed(companyId, costCenterId, vendorId);
    }

    /// <summary>Creates and immediately approves a single-installment payable with the given due date → Scheduled.</summary>
    private static async Task<string> CreateScheduledPayableAsync(HttpClient client, Seed seed, string dueDate, decimal amount = 500m)
    {
        var createRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "AutoPay test payable",
            installments = new[] { new { dueDate, amount } },
        });
        createRes.EnsureSuccessStatusCode();
        var payableId = (await ReadJson(createRes)).GetProperty("id").GetString()!;

        var approveRes = await client.PostAsJsonAsync($"/api/payables/{payableId}/approve", new { });
        approveRes.EnsureSuccessStatusCode();

        return payableId;
    }

    /// <summary>Creates a bank account for the company, optionally marking it as the default paying account.</summary>
    private static async Task<string> CreateBankAccountAsync(HttpClient client, string companyId,
        bool isDefault = false, string method = "Pix", string accountNumber = "00012345")
    {
        var body = isDefault
            ? (object)new
            {
                companyId,
                bankCode = "033",
                bankName = "Banco Santander",
                agency = "1234",
                accountNumber,
                accountDigit = "6",
                accountType = "Checking",
                alias = "Conta AutoPay " + accountNumber,
                openingBalance = 100_000m,
                isDefaultForPayments = true,
                defaultPaymentMethod = method,
            }
            : new
            {
                companyId,
                bankCode = "033",
                bankName = "Banco Santander",
                agency = "1234",
                accountNumber,
                accountDigit = "6",
                accountType = "Checking",
                alias = "Conta AutoPay " + accountNumber,
                openingBalance = 100_000m,
            };

        var res = await client.PostAsJsonAsync("/api/bank-accounts", body);
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("id").GetString()!;
    }

    private static PayableAutoPayJob BuildJob(IServiceProvider services) =>
        new(services.GetRequiredService<IServiceScopeFactory>(),
            services.GetRequiredService<TimeProvider>(),
            services.GetRequiredService<ILogger<PayableAutoPayJob>>(),
            enabled: true,
            interval: TimeSpan.FromHours(24));

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Scheduled_Due_Payable_With_Default_Account_Becomes_Paid_With_System_Actor()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);

        // A bank account marked as the company's default paying account.
        await CreateBankAccountAsync(client, seed.CompanyId, isDefault: true, method: "Pix");

        // A payable with a due date in the past (≤ today) → Scheduled.
        var payableId = await CreateScheduledPayableAsync(client, seed, dueDate: "2026-06-20");

        // Sanity: confirm it's Scheduled before the sweep.
        var before = await ReadJson(await client.GetAsync($"/api/payables/{payableId}"));
        Assert.Equal("Scheduled", before.GetProperty("status").GetString());

        // Act: run the sweep once directly (deterministic, no timer).
        var job = BuildJob(factory.Services);
        await job.RunOnceAsync(CancellationToken.None);

        // Assert: the payable is now Paid.
        var after = await ReadJson(await client.GetAsync($"/api/payables/{payableId}"));
        Assert.Equal("Paid", after.GetProperty("status").GetString());

        // The installment's single payment must be stamped with SystemActor.Id.
        var installment = after.GetProperty("installments")[0];
        Assert.Equal("Paid", installment.GetProperty("status").GetString());
        Assert.Equal(1, installment.GetProperty("payments").GetArrayLength());
        var payment = installment.GetProperty("payments")[0];
        Assert.Equal(SystemActor.Id, payment.GetProperty("createdById").GetGuid());
    }

    [Fact]
    public async Task Scheduled_Due_Payable_Without_Default_Account_Stays_Scheduled()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);

        // No default bank account for this company (no isDefaultForPayments).
        // No per-payable override either.
        var payableId = await CreateScheduledPayableAsync(client, seed, dueDate: "2026-06-20");

        // Act: run the sweep.
        var job = BuildJob(factory.Services);
        await job.RunOnceAsync(CancellationToken.None);

        // Assert: still Scheduled — no source available.
        var after = await ReadJson(await client.GetAsync($"/api/payables/{payableId}"));
        Assert.Equal("Scheduled", after.GetProperty("status").GetString());
        var installment = after.GetProperty("installments")[0];
        Assert.Equal(0, installment.GetProperty("payments").GetArrayLength());
    }

    [Fact]
    public async Task RunOnceAsync_Idempotent_Second_Run_Does_Not_Add_A_Second_Payment()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        await CreateBankAccountAsync(client, seed.CompanyId, isDefault: true, method: "Pix");

        var payableId = await CreateScheduledPayableAsync(client, seed, dueDate: "2026-06-20");

        var job = BuildJob(factory.Services);

        // First sweep — pays the installment.
        await job.RunOnceAsync(CancellationToken.None);
        var afterFirst = await ReadJson(await client.GetAsync($"/api/payables/{payableId}"));
        Assert.Equal("Paid", afterFirst.GetProperty("status").GetString());
        Assert.Equal(1, afterFirst.GetProperty("installments")[0].GetProperty("payments").GetArrayLength());

        // Second sweep — idempotency: already Paid, filtered out by the WHERE clause; no new payment.
        await job.RunOnceAsync(CancellationToken.None);
        var afterSecond = await ReadJson(await client.GetAsync($"/api/payables/{payableId}"));
        Assert.Equal("Paid", afterSecond.GetProperty("status").GetString());
        Assert.Equal(1, afterSecond.GetProperty("installments")[0].GetProperty("payments").GetArrayLength());
    }

    /// <summary>
    /// Isolation test: when processing payable A throws mid-sweep, payable B (which comes next)
    /// still gets paid. Proves that ClearChangeTracker() in the catch prevents the stale tracked
    /// changes from payable A from poisoning the DbContext for payable B.
    ///
    /// Failure injection: we register a test-only IPayablePaymentService decorator via
    /// ConfigureTestServices that throws a DbUpdateConcurrencyException for payable A's id and
    /// delegates to the real service otherwise.
    /// </summary>
    [Fact]
    public async Task RunOnceAsync_FailureOnFirstPayable_DoesNotPreventSecondPayableFromBeingPaid()
    {
        // Use WithWebHostBuilder + ConfigureTestServices so we can register a decorator that
        // injects a deterministic failure for one specific payable ID while allowing the real
        // service to handle all others.
        string? throwForPayableId = null; // set after seeding

        await using var baseFactory = new ApiFactory(fixture.ConnectionString);

        // Seed using the base factory (no decorator — real service handles all calls here).
        var seedClient = await baseFactory.CreateAdminClientAsync();
        var seed = await SeedAsync(seedClient);

        // Both payables share the same company default bank account (Pix).
        await CreateBankAccountAsync(seedClient, seed.CompanyId, isDefault: true, method: "Pix");

        // Payable A → will have the decorator throw; payable B → will be paid normally.
        var payableIdA = await CreateScheduledPayableAsync(seedClient, seed, dueDate: "2026-06-20", amount: 300m);
        var payableIdB = await CreateScheduledPayableAsync(seedClient, seed, dueDate: "2026-06-20", amount: 400m);

        // Now that we have the IDs, tell the decorator to blow up for payable A.
        throwForPayableId = payableIdA;

        // Build a second factory that wires the throwing decorator, pointed at the same DB.
        await using var decoratedFactory = new ApiFactory(fixture.ConnectionString)
            .WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            {
                var descriptor = services.Single(d => d.ServiceType == typeof(IPayablePaymentService));
                services.Remove(descriptor);
                // Re-register the concrete type so we can resolve it inside the decorator factory.
                services.Add(new ServiceDescriptor(descriptor.ImplementationType!, descriptor.ImplementationType!, descriptor.Lifetime));
                services.Add(new ServiceDescriptor(
                    typeof(IPayablePaymentService),
                    sp =>
                    {
                        var inner = (IPayablePaymentService)sp.GetRequiredService(descriptor.ImplementationType!);
                        return new ThrowingPaymentServiceDecorator(inner, () => throwForPayableId);
                    },
                    descriptor.Lifetime));
            }));

        var job = BuildJob(decoratedFactory.Services);
        await job.RunOnceAsync(CancellationToken.None); // must not throw at the sweep level

        // Re-use the seed client to read back the payable state (reads committed DB state).
        // Payable A: still Scheduled — the throw was swallowed and its changes rolled back.
        var afterA = await ReadJson(await seedClient.GetAsync($"/api/payables/{payableIdA}"));
        Assert.Equal("Scheduled", afterA.GetProperty("status").GetString());
        Assert.Equal(0, afterA.GetProperty("installments")[0].GetProperty("payments").GetArrayLength());

        // Payable B: Paid — the sweep continued despite A's failure and committed B cleanly.
        var afterB = await ReadJson(await seedClient.GetAsync($"/api/payables/{payableIdB}"));
        Assert.Equal("Paid", afterB.GetProperty("status").GetString());
        Assert.Equal(1, afterB.GetProperty("installments")[0].GetProperty("payments").GetArrayLength());
    }

    // ---------------------------------------------------------------------------
    // Test helper: IPayablePaymentService decorator that throws for one payable id
    // ---------------------------------------------------------------------------

    private sealed class ThrowingPaymentServiceDecorator(
        IPayablePaymentService inner,
        Func<string?> getThrowForId) : IPayablePaymentService
    {
        public Task<Result<Payable>> RegisterAsync(
            Payable payable, Installment installment, RegisterPaymentInput input,
            Guid createdById, CancellationToken ct)
        {
            var throwId = getThrowForId();
            // The API returns prefixed ids (e.g. "payable_<guid>"); Payable.Id is a raw Guid.
            // Strip the prefix before comparing.
            if (throwId is not null)
            {
                var rawGuid = throwId.Contains('_') ? throwId[(throwId.LastIndexOf('_') + 1)..] : throwId;
                if (Guid.TryParse(rawGuid, out var targetId) && payable.Id == targetId)
                    throw new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException(
                        "Simulated concurrency conflict for isolation test.", []);
            }

            return inner.RegisterAsync(payable, installment, input, createdById, ct);
        }
    }
}
