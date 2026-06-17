using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Persistence;

namespace Ulbra.One.IntegrationTests.Finance;

/// <summary>
/// Locks the optimistic-concurrency contract on the Payable aggregate. The money-critical case:
/// two concurrent operations load the same row, both save, and without a concurrency token both
/// succeed — which on RegisterPayment means real cash is paid twice. The Postgres xmin row-version
/// must make the SECOND SaveChanges throw <see cref="DbUpdateConcurrencyException"/>.
/// </summary>
[Collection("Postgres")]
public class ConcurrencyTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private sealed record Seed(string CompanyId, string CostCenterId, string VendorId);

    private static async Task<Seed> SeedAsync(HttpClient client)
    {
        var cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        var companyRes = await client.PostAsJsonAsync("/api/companies", new { legalName = "ULBRA Teste", cnpj });
        companyRes.EnsureSuccessStatusCode();
        var companyId = (await ReadJson(companyRes)).GetProperty("id").GetString()!;

        var ccRes = await client.PostAsJsonAsync("/api/cost-centers",
            new { companyId, code = "CC1", name = "Marketing", annualBudget = 1_000_000m, autoApprovalLimit = 0m, unitIds = Array.Empty<string>() });
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

    private static async Task<string> CreatePayableAsync(HttpClient client, Seed seed, decimal amount)
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
        return (await ReadJson(createRes)).GetProperty("id").GetString()!;
    }

    /// <summary>
    /// Deterministic concurrency proof against real Postgres xmin: two independent FinanceContext
    /// instances load the SAME payable, A saves first (bumping xmin), then B saves on its now-stale
    /// snapshot and must be rejected.
    /// </summary>
    [Fact]
    public async Task Concurrent_Update_Of_Same_Payable_Second_Save_Throws_DbUpdateConcurrencyException()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var payableId = await CreatePayableAsync(client, seed, 1500m);
        var id = Ulbra.One.Common.Application.Ids.PayableId.Parse(payableId, null).Value;

        using var scopeA = factory.Services.CreateScope();
        using var scopeB = factory.Services.CreateScope();
        var ctxA = scopeA.ServiceProvider.GetRequiredService<AppDbContext>();
        var ctxB = scopeB.ServiceProvider.GetRequiredService<AppDbContext>();

        var payableA = await ctxA.Payables.FirstAsync(p => p.Id == id);
        var payableB = await ctxB.Payables.FirstAsync(p => p.Id == id);

        // A wins: mutates the Payable row and commits, advancing xmin.
        payableA.Approve("user-a", DateTimeOffset.UtcNow);
        await ctxA.SaveChangesAsync();

        // B operates on a now-stale snapshot → must lose.
        payableB.Approve("user-b", DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ctxB.SaveChangesAsync());
    }

    /// <summary>Same proof at the Installment grain — the row RegisterPayment actually mutates.</summary>
    [Fact]
    public async Task Concurrent_Update_Of_Same_Installment_Second_Save_Throws_DbUpdateConcurrencyException()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var payableId = await CreatePayableAsync(client, seed, 1500m);
        var id = Ulbra.One.Common.Application.Ids.PayableId.Parse(payableId, null).Value;

        using var scopeA = factory.Services.CreateScope();
        using var scopeB = factory.Services.CreateScope();
        var ctxA = scopeA.ServiceProvider.GetRequiredService<AppDbContext>();
        var ctxB = scopeB.ServiceProvider.GetRequiredService<AppDbContext>();

        var instA = await ctxA.Installments.FirstAsync(i => i.PayableId == id);
        var instB = await ctxB.Installments.FirstAsync(i => i.PayableId == id);

        ctxA.Entry(instA).Property(nameof(Installment.PaidAmount)).CurrentValue = 1m;
        await ctxA.SaveChangesAsync();

        ctxB.Entry(instB).Property(nameof(Installment.PaidAmount)).CurrentValue = 2m;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ctxB.SaveChangesAsync());
    }
}
