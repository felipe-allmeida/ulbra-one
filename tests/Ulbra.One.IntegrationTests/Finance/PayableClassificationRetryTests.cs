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
/// A payable must NEVER get stuck in Received/intake. When auto-classification can't determine an account —
/// whether the AI answered with nothing ("none") or the provider was unavailable/rate-limited ("unavailable") —
/// the consumer settles it into PendingClassification on the single delivery, with no throw-to-retry (which
/// could otherwise fault on a broker that can't schedule delayed redelivery).
/// </summary>
[Collection("Postgres")]
public class PayableClassificationRetryTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private sealed record Seed(string CompanyId, string CostCenterId, string VendorId);
    private sealed record TokenResponse(string Token);

    /// <summary>Returns a classifier that always produces an empty result (null accounts, 0 confidence).</summary>
    private sealed class EmptyClassifier : IPayableClassifier
    {
        public Task<PayableClassificationResult> ClassifyAsync(Payable payable, CancellationToken ct)
            => Task.FromResult(new PayableClassificationResult(null, 0, "none", "none"));
    }

    /// <summary>Classifier reporting the AI provider as unavailable (down / rate-limited / not configured).</summary>
    private sealed class UnavailableClassifier : IPayableClassifier
    {
        public Task<PayableClassificationResult> ClassifyAsync(Payable payable, CancellationToken ct)
            => Task.FromResult(new PayableClassificationResult(null, 0, "AI provider unavailable.", ClassificationSources.Unavailable));
    }

    private static async Task<HttpClient> CreateAdminClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/dev-token", new { email = "admin@ulbra.br", name = "Admin Teste" });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return client;
    }

    private static async Task<Seed> SeedAsync(HttpClient client)
    {
        var cnpj = TestDocuments.RandomCnpj();
        var companyRes = await client.PostAsJsonAsync("/api/companies",
            new { legalName = "ULBRA Retry Test", cnpj });
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
            name = "Fornecedor Retry",
            tradeName = "Retry Ltda",
            document = TestCnpj.New(),
            email = "retry@test.com",
            phone = "5133334444",
            address = (object?)null,
            paymentTerms = "30 dias",
            bankInfo = (string?)null,
        });
        vendorRes.EnsureSuccessStatusCode();
        var vendorId = (await ReadJson(vendorRes)).GetProperty("id").GetString()!;

        return new Seed(companyId, costCenterId, vendorId);
    }

    private static async Task<(string PrefixedId, Guid RawId)> CreatePayableAsync(HttpClient client, Seed seed)
    {
        var res = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Retry test payable",
            installments = new[] { new { dueDate = "2026-07-01", amount = 100m } },
        });
        res.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var body = await ReadJson(res);
        var prefixedId = body.GetProperty("id").GetString()!;
        var rawId = Guid.Parse(prefixedId.Split('_')[1]);
        return (prefixedId, rawId);
    }

    [Fact]
    public async Task No_suggestion_settles_into_PendingClassification_never_stuck_in_Received()
    {
        // Empty classifier ("none"): AI answered with nothing and there's no vendor history.
        await using var factory = new ApiFactory(fixture.ConnectionString)
            .WithWebHostBuilder(b => b.ConfigureServices(s =>
            {
                s.RemoveAll<IPayableClassifier>();
                s.AddScoped<IPayableClassifier>(_ => new EmptyClassifier());
            }));

        var client = await CreateAdminClientAsync(factory);
        var seed = await SeedAsync(client);
        var (prefixedId, rawId) = await CreatePayableAsync(client, seed);

        // Single delivery — must NOT throw, must settle out of Received.
        await using var scope = factory.Services.CreateAsyncScope();
        var consumer = scope.ServiceProvider.GetRequiredService<PayableReceivedConsumer>();
        await consumer.ProcessAsync(rawId, CancellationToken.None);

        var body = await ReadJson(await client.GetAsync($"/api/payables/{prefixedId}"));
        Assert.Equal("PendingClassification", body.GetProperty("status").GetString());
        Assert.False(body.GetProperty("isClassified").GetBoolean());
    }

    [Fact]
    public async Task ProviderUnavailable_settles_into_PendingClassification_without_retry()
    {
        // Provider unavailable (429/overloaded/down): must NOT throw-to-retry (which could fault on a broker
        // without delayed-message support and strand the payable) — it settles for a human on the first pass.
        await using var factory = new ApiFactory(fixture.ConnectionString)
            .WithWebHostBuilder(b => b.ConfigureServices(s =>
            {
                s.RemoveAll<IPayableClassifier>();
                s.AddScoped<IPayableClassifier>(_ => new UnavailableClassifier());
            }));

        var client = await CreateAdminClientAsync(factory);
        var seed = await SeedAsync(client);
        var (prefixedId, rawId) = await CreatePayableAsync(client, seed);

        await using var scope = factory.Services.CreateAsyncScope();
        var consumer = scope.ServiceProvider.GetRequiredService<PayableReceivedConsumer>();
        await consumer.ProcessAsync(rawId, CancellationToken.None); // no throw

        var body = await ReadJson(await client.GetAsync($"/api/payables/{prefixedId}"));
        Assert.Equal("PendingClassification", body.GetProperty("status").GetString());
        Assert.False(body.GetProperty("isClassified").GetBoolean());
    }
}
