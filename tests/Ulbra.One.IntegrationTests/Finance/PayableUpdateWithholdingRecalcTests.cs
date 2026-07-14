using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Finance.Infrastructure.Messaging;
using Ulbra.One.Fiscal.Domain;
using Ulbra.One.Fiscal.Domain.Entities;
using Ulbra.One.Persistence;

namespace Ulbra.One.IntegrationTests.Finance;

/// <summary>
/// AEL-696: editing a payable publishes PayableUpdated (captured by the EF bus outbox) and
/// PayableUpdatedConsumer recalculates withholdings. The consumer is driven directly (no broker,
/// mirrors PayableAutoClassifyTests); the withholding calculator is the real Fiscal one, running
/// against the group-wide rules auto-seeded on startup — assertions are structural so seeded
/// rates can evolve without breaking these tests.
/// </summary>
[Collection("Postgres")]
public class PayableUpdateWithholdingRecalcTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private sealed record Seed(string CompanyId, string CostCenterId, string VendorId);

    private static async Task<Seed> SeedAsync(HttpClient client)
    {
        var cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        var companyRes = await client.PostAsJsonAsync("/api/companies",
            new { legalName = "ULBRA Recalc Test", cnpj });
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
            name = "Fornecedor Recalc",
            tradeName = "Recalc Ltda",
            document = "11.222.333/0001-81",
            email = "recalc@test.com",
            phone = "5133334444",
            address = (object?)null,
            paymentTerms = "30 dias",
            bankInfo = (string?)null,
        });
        vendorRes.EnsureSuccessStatusCode();
        var vendorId = (await ReadJson(vendorRes)).GetProperty("id").GetString()!;

        return new Seed(companyId, costCenterId, vendorId);
    }

    private static async Task<(string PrefixedId, Guid RawId)> CreatePayableAsync(
        HttpClient client, Seed seed, object[] installments, string? type)
    {
        var res = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "NF para recálculo de retenções",
            installments,
            type,
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var prefixedId = (await ReadJson(res)).GetProperty("id").GetString()!;
        return (prefixedId, Guid.Parse(prefixedId.Split('_')[1]));
    }

    private static Task<HttpResponseMessage> UpdatePayableAsync(
        HttpClient client, string prefixedId, Seed seed, object[] installments, string? type,
        string description = "NF editada") =>
        client.PutAsJsonAsync($"/api/payables/{prefixedId}", new
        {
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description,
            installments,
            type,
        });

    private static async Task RunConsumerAsync(ApiFactory factory, Guid rawId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var consumer = scope.ServiceProvider.GetRequiredService<PayableUpdatedConsumer>();
        await consumer.ProcessAsync(rawId, CancellationToken.None);
    }

    /// <summary>Counts PayableUpdated outbox rows for one specific payable — parallel-safe.</summary>
    private static async Task<int> CountOutboxRowsAsync(ApiFactory factory, Guid rawId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*)::int AS \"Value\" FROM masstransit.outbox_message " +
                "WHERE message_type LIKE '%PayableUpdated%' AND body::text LIKE {0}",
                $"%{rawId}%")
            .SingleAsync();
    }

    private static object[] SingleInstallment(decimal amount) =>
        [new { dueDate = "2026-08-10", amount }];

    [Fact]
    public async Task Update_success_writes_PayableUpdated_to_outbox()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var (prefixedId, rawId) = await CreatePayableAsync(client, seed, SingleInstallment(1000m), type: "Product");

        var updateRes = await UpdatePayableAsync(client, prefixedId, seed, SingleInstallment(1000m), type: "Product");
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);

        Assert.Equal(1, await CountOutboxRowsAsync(factory, rawId));
    }

    [Fact]
    public async Task Update_ProductToService_consumer_recalculates_withholdings()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        // 10_000 keeps every seeded federal tax above its de-minimis threshold.
        var (prefixedId, rawId) = await CreatePayableAsync(client, seed, SingleInstallment(10_000m), type: "Product");

        var beforeBody = await ReadJson(await client.GetAsync($"/api/payables/{prefixedId}"));
        Assert.Equal(0, beforeBody.GetProperty("withholdings").GetArrayLength());

        var updateRes = await UpdatePayableAsync(client, prefixedId, seed, SingleInstallment(10_000m), type: "Service");
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);
        await RunConsumerAsync(factory, rawId);

        var body = await ReadJson(await client.GetAsync($"/api/payables/{prefixedId}"));
        Assert.Equal("Service", body.GetProperty("type").GetString());
        var withholdings = body.GetProperty("withholdings").EnumerateArray().ToList();
        Assert.NotEmpty(withholdings);
        Assert.Contains(withholdings, w => w.GetProperty("taxType").GetString() == "Irrf");
        var totalWithheld = body.GetProperty("totalWithheld").GetDecimal();
        Assert.True(totalWithheld > 0);
        Assert.Equal(10_000m - totalWithheld, body.GetProperty("netTotal").GetDecimal());
        var netSum = body.GetProperty("installments").EnumerateArray()
            .Sum(i => i.GetProperty("netAmount").GetDecimal());
        Assert.Equal(body.GetProperty("netTotal").GetDecimal(), netSum);
    }

    [Fact]
    public async Task Consumer_applies_vendor_specific_rule_over_system_default()
    {
        // Proves VendorId flows end-to-end: AutoWithholdingService → WithholdingCalculator → resolver.
        // A vendor-specific IRRF rule (5%) must beat the seeded system default (1,5%).
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var vendorRawId = Guid.Parse(seed.VendorId.Split('_')[1]);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.AddAsync(
                new WithholdingRule(WithholdingTaxType.Irrf, retains: true, RateSource.Fixed, 0.05m,
                    companyId: null, vendorId: vendorRawId),
                CancellationToken.None);
            await uow.SaveChangesAsync(CancellationToken.None);
        }

        var (prefixedId, rawId) = await CreatePayableAsync(client, seed, SingleInstallment(10_000m), type: "Product");
        var updateRes = await UpdatePayableAsync(client, prefixedId, seed, SingleInstallment(10_000m), type: "Service");
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);
        await RunConsumerAsync(factory, rawId);

        var body = await ReadJson(await client.GetAsync($"/api/payables/{prefixedId}"));
        var irrf = body.GetProperty("withholdings").EnumerateArray()
            .Single(w => w.GetProperty("taxType").GetString() == "Irrf");
        Assert.Equal(500m, irrf.GetProperty("amount").GetDecimal()); // 5% do fornecedor, não 1,5% do sistema
    }

    [Fact]
    public async Task Update_ServiceToProduct_consumer_clears_withholdings_and_restores_net()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        object[] installments =
        [
            new { dueDate = "2026-08-10", amount = 600m },
            new { dueDate = "2026-09-10", amount = 400m },
        ];
        var (prefixedId, rawId) = await CreatePayableAsync(client, seed, installments, type: "Service");

        // Deterministic manual withholding: IRRF 150 on 1000 → nets 510/340.
        var setRes = await client.PutAsJsonAsync($"/api/payables/{prefixedId}/withholdings", new
        {
            items = new[] { new { taxType = "Irrf", baseAmount = 1000m, rate = 0.15m, amount = 150m } },
        });
        Assert.Equal(HttpStatusCode.OK, setRes.StatusCode);
        Assert.Equal(150m, (await ReadJson(setRes)).GetProperty("totalWithheld").GetDecimal());

        var updateRes = await UpdatePayableAsync(client, prefixedId, seed, installments, type: "Product");
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);
        await RunConsumerAsync(factory, rawId);

        var body = await ReadJson(await client.GetAsync($"/api/payables/{prefixedId}"));
        Assert.Equal("Product", body.GetProperty("type").GetString());
        Assert.Equal(0, body.GetProperty("withholdings").GetArrayLength());
        Assert.Equal(0m, body.GetProperty("totalWithheld").GetDecimal());
        Assert.Equal(1000m, body.GetProperty("netTotal").GetDecimal());
        var nets = body.GetProperty("installments").EnumerateArray()
            .Select(i => i.GetProperty("netAmount").GetDecimal())
            .OrderBy(a => a).ToArray();
        Assert.Equal([400m, 600m], nets);
    }

    [Fact]
    public async Task ProcessAsync_twice_is_idempotent()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var (prefixedId, rawId) = await CreatePayableAsync(client, seed, SingleInstallment(10_000m), type: "Product");

        var updateRes = await UpdatePayableAsync(client, prefixedId, seed, SingleInstallment(10_000m), type: "Service");
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);

        // Two deliveries in separate scopes — simulates at-least-once redelivery.
        await RunConsumerAsync(factory, rawId);
        var afterFirst = await ReadJson(await client.GetAsync($"/api/payables/{prefixedId}"));
        await RunConsumerAsync(factory, rawId);
        var afterSecond = await ReadJson(await client.GetAsync($"/api/payables/{prefixedId}"));

        Assert.Equal(afterFirst.GetProperty("withholdings").GetArrayLength(),
            afterSecond.GetProperty("withholdings").GetArrayLength());
        Assert.Equal(afterFirst.GetProperty("totalWithheld").GetDecimal(),
            afterSecond.GetProperty("totalWithheld").GetDecimal());
        // No duplicated rows: one row per tax type.
        var taxTypes = afterSecond.GetProperty("withholdings").EnumerateArray()
            .Select(w => w.GetProperty("taxType").GetString()).ToList();
        Assert.Equal(taxTypes.Distinct().Count(), taxTypes.Count);
    }

    [Fact]
    public async Task Consumer_noop_on_scheduled_payable_keeps_locked_withholdings()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);

        // Build the Scheduled payable entirely via domain in one scope (no HTTP): the consumer's
        // Scheduled guard must fire before the Product-clear, keeping the booked withholding.
        Guid payableId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
            var payable = new Payable(Guid.NewGuid(), PayableOrigin.Manual, Guid.NewGuid(),
                "Fornecedor Scheduled", documentNumber: null, issueDate: null,
                description: "Scheduled com retenção", costCenterId: Guid.NewGuid(), categoryId: null,
                installments: [new Installment(1, new DateOnly(2026, 8, 10), 1000m)]);
            payable.SetType(PayableType.Product);
            payable.SetWithholdings([(WithholdingTaxType.Irrf, 1000m, null, 150m)]);
            payable.Approve("test-user", now);
            payable.CompleteAccounting("test-user", now, hasFiscalDocument: true);
            await uow.AddAsync(payable, CancellationToken.None);
            await uow.SaveChangesAsync(CancellationToken.None);
            payableId = payable.Id;
        }

        await RunConsumerAsync(factory, payableId);

        await using var assertScope = factory.Services.CreateAsyncScope();
        var reloaded = await assertScope.ServiceProvider.GetRequiredService<IUnitOfWork>()
            .ReadSet<Payable>().FirstAsync(p => p.Id == payableId);
        Assert.Equal(PayableStatus.Scheduled, reloaded.Status);
        var w = Assert.Single(reloaded.Withholdings);
        Assert.Equal(150m, w.Amount);
        Assert.Equal(850m, reloaded.Installments.Single().NetAmount);
    }

    [Fact]
    public async Task Update_failure_does_not_publish()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var (prefixedId, rawId) = await CreatePayableAsync(client, seed, SingleInstallment(1000m), type: "Product");

        var rejectRes = await client.PostAsJsonAsync($"/api/payables/{prefixedId}/reject", new { reason = "Rejeitado" });
        Assert.Equal(HttpStatusCode.OK, rejectRes.StatusCode);

        var updateRes = await UpdatePayableAsync(client, prefixedId, seed, SingleInstallment(1000m), type: "Service");
        Assert.Equal(HttpStatusCode.Conflict, updateRes.StatusCode);

        Assert.Equal(0, await CountOutboxRowsAsync(factory, rawId));
    }
}
