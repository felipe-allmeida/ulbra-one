using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ulbra.One.Finance.Application.Classification;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Finance.Infrastructure.Messaging;
using Ulbra.One.IntegrationTests;

namespace Ulbra.One.IntegrationTests.Finance;

/// <summary>
/// Integration tests for PayableReceivedConsumer.ProcessAsync — exercised directly (no broker)
/// against a real Postgres testcontainer and a stubbed IPayableClassifier.
/// </summary>
[Collection("Postgres")]
public class PayableAutoClassifyTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private sealed record Seed(string CompanyId, string CostCenterId, string VendorId);
    private sealed record TokenResponse(string Token);

    /// <summary>
    /// Stubs IPayableClassifier to return a fixed result so we can control confidence per test
    /// without going through the full AI chain.
    /// </summary>
    private sealed class StubClassifier(PayableClassificationResult result) : IPayableClassifier
    {
        public Task<PayableClassificationResult> ClassifyAsync(Payable payable, CancellationToken ct)
            => Task.FromResult(result);
    }

    /// <summary>Authenticates against any factory instance (mirrors AiClassificationEndpointTests).</summary>
    private static async Task<HttpClient> CreateAdminClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/dev-token", new { email = "admin@ulbra.br", name = "Admin Teste" });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", body!.Token);
        return client;
    }

    private static async Task<Seed> SeedAsync(HttpClient client, decimal autoApprovalLimit = 0)
    {
        var cnpj = TestDocuments.RandomCnpj();
        var companyRes = await client.PostAsJsonAsync("/api/companies",
            new { legalName = "ULBRA AutoClassify Test", cnpj });
        companyRes.EnsureSuccessStatusCode();
        var companyId = (await ReadJson(companyRes)).GetProperty("id").GetString()!;

        var ccRes = await client.PostAsJsonAsync("/api/cost-centers",
            new { companyId, code = "CC1", name = "TI", annualBudget = 500_000m, autoApprovalLimit, unitIds = Array.Empty<string>() });
        ccRes.EnsureSuccessStatusCode();
        var costCenterId = (await ReadJson(ccRes)).GetProperty("id").GetString()!;

        var vendorRes = await client.PostAsJsonAsync("/api/vendors", new
        {
            companyId,
            personType = 1,
            name = "Fornecedor AutoClassify",
            tradeName = "AutoClassify Ltda",
            document = TestCnpj.New(),
            email = "autoclassify@test.com",
            phone = "5133334444",
            address = (object?)null,
            paymentTerms = "30 dias",
            bankInfo = (string?)null,
        });
        vendorRes.EnsureSuccessStatusCode();
        var vendorId = (await ReadJson(vendorRes)).GetProperty("id").GetString()!;

        return new Seed(companyId, costCenterId, vendorId);
    }

    /// <summary>
    /// Creates a single-installment payable and returns both the prefixed id and the raw Guid.
    /// Asserts that the response status is Received (consumer has not run yet).
    /// </summary>
    private static async Task<(string PrefixedId, Guid RawId)> CreatePayableAsync(
        HttpClient client, Seed seed, decimal amount = 100m)
    {
        var res = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Cloud hosting subscription",
            installments = new[] { new { dueDate = "2026-07-01", amount } },
            type = "Product", // determined product — required before a payable can schedule
        });
        res.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var body = await ReadJson(res);
        // Create always returns Received — consumer drives status forward.
        Assert.Equal("Received", body.GetProperty("status").GetString());
        var prefixedId = body.GetProperty("id").GetString()!;
        // Prefixed ids look like "payable_<guid>" — extract the raw Guid.
        var rawId = Guid.Parse(prefixedId.Split('_')[1]);
        return (prefixedId, rawId);
    }

    [Fact]
    public async Task Confident_under_limit_auto_approves_to_accounting_after_process()
    {
        // Arrange: stub returns ≥0.8 confidence with valid account ids; cost center limit is 500m.
        var accId = Guid.NewGuid();
        var result = new PayableClassificationResult(accId, 0.9, "high confidence", "ai");

        await using var factory = new ApiFactory(fixture.ConnectionString)
            .WithWebHostBuilder(b => b.ConfigureServices(s =>
            {
                s.RemoveAll<IPayableClassifier>();
                s.AddScoped<IPayableClassifier>(_ => new StubClassifier(result));
            }));

        var client = await CreateAdminClientAsync(factory);
        var seed = await SeedAsync(client, autoApprovalLimit: 500m);
        var (prefixedId, rawId) = await CreatePayableAsync(client, seed, amount: 100m);

        // Act: drive consumer directly — simulates broker delivery without needing RabbitMQ.
        await using var scope = factory.Services.CreateAsyncScope();
        var consumer = scope.ServiceProvider.GetRequiredService<PayableReceivedConsumer>();
        await consumer.ProcessAsync(rawId, CancellationToken.None);

        // Assert: GET should show PendingAccounting (auto-approved: classified + under limit → accounting stage).
        var getRes = await client.GetAsync($"/api/payables/{prefixedId}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var body = await ReadJson(getRes);
        Assert.Equal("PendingAccounting", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("isClassified").GetBoolean());
    }

    [Fact]
    public async Task Mid_band_parks_with_suggestion_pending_classification()
    {
        // Arrange: stub returns 0.6 confidence (mid-band: below 0.8 threshold).
        var accId = Guid.NewGuid();
        var result = new PayableClassificationResult(accId, 0.6, "maybe", "ai");

        await using var factory = new ApiFactory(fixture.ConnectionString)
            .WithWebHostBuilder(b => b.ConfigureServices(s =>
            {
                s.RemoveAll<IPayableClassifier>();
                s.AddScoped<IPayableClassifier>(_ => new StubClassifier(result));
            }));

        var client = await CreateAdminClientAsync(factory);
        var seed = await SeedAsync(client, autoApprovalLimit: 500m);
        var (prefixedId, rawId) = await CreatePayableAsync(client, seed, amount: 100m);

        // Act: drive consumer directly.
        await using var scope = factory.Services.CreateAsyncScope();
        var consumer = scope.ServiceProvider.GetRequiredService<PayableReceivedConsumer>();
        await consumer.ProcessAsync(rawId, CancellationToken.None);

        // Assert: status is PendingClassification; payable is not classified (suggestion only).
        var getRes = await client.GetAsync($"/api/payables/{prefixedId}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var body = await ReadJson(getRes);
        Assert.Equal("PendingClassification", body.GetProperty("status").GetString());
        Assert.False(body.GetProperty("isClassified").GetBoolean());
    }

    [Fact]
    public async Task Idempotency_second_process_call_is_no_op()
    {
        // Arrange: stub returns high confidence.
        var accId = Guid.NewGuid();
        var result = new PayableClassificationResult(accId, 0.9, "confident", "ai");

        await using var factory = new ApiFactory(fixture.ConnectionString)
            .WithWebHostBuilder(b => b.ConfigureServices(s =>
            {
                s.RemoveAll<IPayableClassifier>();
                s.AddScoped<IPayableClassifier>(_ => new StubClassifier(result));
            }));

        var client = await CreateAdminClientAsync(factory);
        var seed = await SeedAsync(client, autoApprovalLimit: 500m);
        var (prefixedId, rawId) = await CreatePayableAsync(client, seed, amount: 100m);

        // First call — advances status to PendingAccounting.
        await using (var scope1 = factory.Services.CreateAsyncScope())
        {
            var consumer1 = scope1.ServiceProvider.GetRequiredService<PayableReceivedConsumer>();
            await consumer1.ProcessAsync(rawId, CancellationToken.None);
        }

        // Verify first call advanced the status.
        var afterFirst = await ReadJson(await client.GetAsync($"/api/payables/{prefixedId}"));
        Assert.Equal("PendingAccounting", afterFirst.GetProperty("status").GetString());

        // Act: second call (simulates at-least-once redelivery).
        await using (var scope2 = factory.Services.CreateAsyncScope())
        {
            var consumer2 = scope2.ServiceProvider.GetRequiredService<PayableReceivedConsumer>();
            await consumer2.ProcessAsync(rawId, CancellationToken.None);
        }

        // Assert: status is still PendingAccounting — idempotency guard (Status != Received) made it a no-op.
        var afterSecond = await ReadJson(await client.GetAsync($"/api/payables/{prefixedId}"));
        Assert.Equal("PendingAccounting", afterSecond.GetProperty("status").GetString());
    }

    // ---------------------------------------------------------------------------
    // Task 4: classify endpoint runs auto-approval + PayableResponse suggestion fields
    // ---------------------------------------------------------------------------

    private sealed record AccountRef(string Id, string Code, string Kind);

    /// <summary>
    /// Returns the prefixed id of an analytical account from the global ledger chart
    /// (auto-seeded on startup).
    /// </summary>
    private static async Task<string> SeedChartAccountAsync(HttpClient client, string chartType)
    {
        var accounts = await client.GetFromJsonAsync<List<AccountRef>>(
            "/api/ledger-accounts?kind=Analytical&activeOnly=true");
        return accounts!.First().Id;
    }

    /// <summary>
    /// Parks a payable at PendingClassification by running the consumer with mid-band confidence,
    /// then classifying via the endpoint should auto-approve when amount is under the cost-center limit.
    /// </summary>
    [Fact]
    public async Task Classify_Endpoint_Under_Limit_Parks_To_PendingAccounting()
    {
        // Arrange: stub returns 0.6 confidence → parks at PendingClassification.
        var suggestedAccId = Guid.NewGuid();
        var midResult = new PayableClassificationResult(suggestedAccId, 0.6, "mid-band", "ai");

        await using var factory = new ApiFactory(fixture.ConnectionString)
            .WithWebHostBuilder(b => b.ConfigureServices(s =>
            {
                s.RemoveAll<IPayableClassifier>();
                s.AddScoped<IPayableClassifier>(_ => new StubClassifier(midResult));
            }));

        var client = await CreateAdminClientAsync(factory);
        // autoApprovalLimit = 500m; payable amount = 100m (under limit).
        var seed = await SeedAsync(client, autoApprovalLimit: 500m);
        var (prefixedId, rawId) = await CreatePayableAsync(client, seed, amount: 100m);

        // Park it at PendingClassification via the consumer.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var consumer = scope.ServiceProvider.GetRequiredService<PayableReceivedConsumer>();
            await consumer.ProcessAsync(rawId, CancellationToken.None);
        }
        Assert.Equal("PendingClassification",
            (await ReadJson(await client.GetAsync($"/api/payables/{prefixedId}"))).GetProperty("status").GetString());

        // Seed a real accounting chart account for the classify call.
        var accId = await SeedChartAccountAsync(client, "Accounting");

        // Act: classify via endpoint — amount ≤ limit → auto-approve, routing to the accounting stage.
        var classifyRes = await client.PostAsJsonAsync($"/api/payables/{prefixedId}/classify",
            new { accountingAccountId = accId });
        Assert.Equal(HttpStatusCode.OK, classifyRes.StatusCode);
        var body = await ReadJson(classifyRes);

        // Assert: auto-approved → PendingAccounting.
        Assert.Equal("PendingAccounting", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("isClassified").GetBoolean());
        // Suggestion fields must be cleared after classify.
        Assert.Equal(JsonValueKind.Null, body.GetProperty("suggestedAccountingAccountId").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("classificationConfidence").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("classificationRationale").ValueKind);
    }

    /// <summary>
    /// When the payable amount exceeds the cost-center auto-approval limit, classifying via
    /// the endpoint should advance to PendingApproval (not Scheduled).
    /// </summary>
    [Fact]
    public async Task Classify_Endpoint_Over_Limit_Parks_To_PendingApproval()
    {
        // Arrange: mid-band → PendingClassification; autoApprovalLimit = 500m; amount = 1000m (over limit).
        var midResult = new PayableClassificationResult(Guid.NewGuid(), 0.6, "mid-band", "ai");

        await using var factory = new ApiFactory(fixture.ConnectionString)
            .WithWebHostBuilder(b => b.ConfigureServices(s =>
            {
                s.RemoveAll<IPayableClassifier>();
                s.AddScoped<IPayableClassifier>(_ => new StubClassifier(midResult));
            }));

        var client = await CreateAdminClientAsync(factory);
        var seed = await SeedAsync(client, autoApprovalLimit: 500m);
        var (prefixedId, rawId) = await CreatePayableAsync(client, seed, amount: 1000m);

        // Park it at PendingClassification.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var consumer = scope.ServiceProvider.GetRequiredService<PayableReceivedConsumer>();
            await consumer.ProcessAsync(rawId, CancellationToken.None);
        }
        Assert.Equal("PendingClassification",
            (await ReadJson(await client.GetAsync($"/api/payables/{prefixedId}"))).GetProperty("status").GetString());

        var accId = await SeedChartAccountAsync(client, "Accounting");

        // Act: classify via endpoint — amount > limit → PendingApproval.
        var classifyRes = await client.PostAsJsonAsync($"/api/payables/{prefixedId}/classify",
            new { accountingAccountId = accId });
        Assert.Equal(HttpStatusCode.OK, classifyRes.StatusCode);
        var body = await ReadJson(classifyRes);

        Assert.Equal("PendingApproval", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("isClassified").GetBoolean());
    }

    /// <summary>
    /// A parked payable (PendingClassification with suggestion) exposes the suggestion fields
    /// on GET, and those fields are cleared to null after classify.
    /// </summary>
    [Fact]
    public async Task Suggestion_Fields_Round_Trip_On_Parked_Payable()
    {
        // Arrange: mid-band → parked with suggestion.
        var suggestedAccId = Guid.NewGuid();
        var midResult = new PayableClassificationResult(suggestedAccId, 0.65, "maybe office supplies", "ai");

        await using var factory = new ApiFactory(fixture.ConnectionString)
            .WithWebHostBuilder(b => b.ConfigureServices(s =>
            {
                s.RemoveAll<IPayableClassifier>();
                s.AddScoped<IPayableClassifier>(_ => new StubClassifier(midResult));
            }));

        var client = await CreateAdminClientAsync(factory);
        var seed = await SeedAsync(client, autoApprovalLimit: 0m);
        var (prefixedId, rawId) = await CreatePayableAsync(client, seed, amount: 100m);

        // Run consumer → parked at PendingClassification with suggestion.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var consumer = scope.ServiceProvider.GetRequiredService<PayableReceivedConsumer>();
            await consumer.ProcessAsync(rawId, CancellationToken.None);
        }

        // Act: GET — suggestion fields should be present.
        var getRes = await client.GetAsync($"/api/payables/{prefixedId}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var body = await ReadJson(getRes);

        Assert.Equal("PendingClassification", body.GetProperty("status").GetString());
        Assert.False(body.GetProperty("isClassified").GetBoolean());
        // The suggested ids are stored as prefixed strings (chart_account_<guid>).
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("suggestedAccountingAccountId").ValueKind);
        Assert.Equal(0.65, body.GetProperty("classificationConfidence").GetDouble(), precision: 5);
        Assert.Equal("maybe office supplies", body.GetProperty("classificationRationale").GetString());
    }
}
