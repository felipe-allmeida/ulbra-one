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

namespace Ulbra.One.IntegrationTests.Finance;

/// <summary>
/// Tests for the throw-vs-settle logic added by the escalating retry feature.
/// ProcessAsync(id, giveUpIfUnclassified: false) must throw ClassificationUnavailableException
/// when the classifier returns nothing; (true) must settle into PendingClassification.
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
            => Task.FromResult(new PayableClassificationResult(null, null, 0, "none", "none"));
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
        var cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
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
            document = "11.222.333/0001-81",
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
    public async Task GiveUp_false_with_empty_classifier_throws_and_payable_stays_Received()
    {
        // Arrange: use an empty classifier (no suggestion, no accounts).
        await using var factory = new ApiFactory(fixture.ConnectionString)
            .WithWebHostBuilder(b => b.ConfigureServices(s =>
            {
                s.RemoveAll<IPayableClassifier>();
                s.AddScoped<IPayableClassifier>(_ => new EmptyClassifier());
            }));

        var client = await CreateAdminClientAsync(factory);
        var seed = await SeedAsync(client);
        var (prefixedId, rawId) = await CreatePayableAsync(client, seed);

        // Act: call with giveUpIfUnclassified = false — should throw ClassificationUnavailableException.
        await using var scope = factory.Services.CreateAsyncScope();
        var consumer = scope.ServiceProvider.GetRequiredService<PayableReceivedConsumer>();

        await Assert.ThrowsAsync<ClassificationUnavailableException>(
            () => consumer.ProcessAsync(rawId, giveUpIfUnclassified: false, CancellationToken.None));

        // Assert: payable must still be in Received (no state mutation before the throw).
        var getRes = await client.GetAsync($"/api/payables/{prefixedId}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var body = await ReadJson(getRes);
        Assert.Equal("Received", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task GiveUp_true_with_empty_classifier_settles_to_PendingClassification()
    {
        // Arrange: use an empty classifier (no suggestion, no accounts).
        await using var factory = new ApiFactory(fixture.ConnectionString)
            .WithWebHostBuilder(b => b.ConfigureServices(s =>
            {
                s.RemoveAll<IPayableClassifier>();
                s.AddScoped<IPayableClassifier>(_ => new EmptyClassifier());
            }));

        var client = await CreateAdminClientAsync(factory);
        var seed = await SeedAsync(client);
        var (prefixedId, rawId) = await CreatePayableAsync(client, seed);

        // Act: call with giveUpIfUnclassified = true (final attempt behaviour) — no throw expected.
        await using var scope = factory.Services.CreateAsyncScope();
        var consumer = scope.ServiceProvider.GetRequiredService<PayableReceivedConsumer>();
        await consumer.ProcessAsync(rawId, giveUpIfUnclassified: true, CancellationToken.None);

        // Assert: payable must be settled at PendingClassification (human must classify).
        var getRes = await client.GetAsync($"/api/payables/{prefixedId}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var body = await ReadJson(getRes);
        Assert.Equal("PendingClassification", body.GetProperty("status").GetString());
        Assert.False(body.GetProperty("isClassified").GetBoolean());
    }
}
