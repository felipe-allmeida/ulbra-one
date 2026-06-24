using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ulbra.One.Common.Application.Ai;
using Ulbra.One.Common.Domain;

namespace Ulbra.One.IntegrationTests.Finance;

[Collection("Postgres")]
public class AiClassificationEndpointTests(PostgresFixture fixture)
{
    // Returns a fixed suggestion regardless of T (T is always PayableClassificationSuggestion here).
    private sealed class StubAiCompletion(string? accountingCode, string? financialCode) : IAiCompletion
    {
        public Task<Result<TResult>> CompleteAsync<TResult>(
            AiPrompt prompt, AiOptions? options = null, CancellationToken ct = default)
            where TResult : class
        {
            object suggestion = Activator.CreateInstance(
                typeof(TResult), accountingCode, financialCode, 0.9, "stub")!;
            return Task.FromResult(Result.Success((TResult)suggestion));
        }
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private sealed record AiSuggestionDto(
        string? AccountingAccountId, string? FinancialAccountId,
        double Confidence, string Rationale, string Source);

    private sealed record ChartAccountDto(string Id, string Code, string ChartType, string Kind, bool IsActive);

    private sealed record Seed(string CompanyId, string CostCenterId, string VendorId);

    private sealed record TokenResponse(string Token);

    /// <summary>Authenticates a client from any WebApplicationFactory instance.</summary>
    private static async Task<HttpClient> CreateAdminClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/dev-token", new { email = "admin@ulbra.br", name = "Admin Teste" });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return client;
    }

    /// <summary>Seeds a company, cost center, and vendor via the public API — same pattern as PayableEndpointsTests.</summary>
    private static async Task<Seed> SeedAsync(HttpClient client)
    {
        var cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        var companyRes = await client.PostAsJsonAsync("/api/companies", new { legalName = "ULBRA AI Test", cnpj });
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
            name = "Acme Cloud Ltda",
            tradeName = "Acme Cloud",
            document = "11.222.333/0001-81",
            email = "contato@acme.com",
            phone = "5133334444",
            address = (object?)null,
            paymentTerms = "30 dias",
            bankInfo = (string?)null,
        });
        vendorRes.EnsureSuccessStatusCode();
        var vendorId = (await ReadJson(vendorRes)).GetProperty("id").GetString()!;

        return new Seed(companyId, costCenterId, vendorId);
    }

    /// <summary>Creates a minimal payable and returns its prefixed id.</summary>
    private static async Task<string> CreatePayableAsync(HttpClient client, Seed seed)
    {
        var create = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            description = "Cloud hosting subscription",
            costCenterId = seed.CostCenterId,
            installments = new[] { new { dueDate = "2026-07-01", amount = 1234.56m } },
        });
        create.EnsureSuccessStatusCode();
        return (await ReadJson(create)).GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Ai_path_returns_resolved_accounts_with_source_ai()
    {
        // Use a plain factory to read chart accounts (global, no company scope).
        await using var setupFactory = new ApiFactory(fixture.ConnectionString);
        var setupClient = await setupFactory.CreateAdminClientAsync();

        // Pick a seeded analytical accounting + financial account code to feed the stub.
        var accounts = await setupClient.GetFromJsonAsync<List<ChartAccountDto>>("/api/chart-accounts");
        var acc = accounts!.First(a => a.ChartType == "Accounting" && a.Kind == "Analytical" && a.IsActive);
        var fin = accounts!.First(a => a.ChartType == "Financial" && a.Kind == "Analytical" && a.IsActive);

        // Wire up a factory with the stub IAiCompletion replacing the default Fake provider.
        await using var aiFactory = new ApiFactory(fixture.ConnectionString)
            .WithWebHostBuilder(b => b.ConfigureServices(s =>
            {
                s.RemoveAll<IAiCompletion>();
                s.AddScoped<IAiCompletion>(_ => new StubAiCompletion(acc.Code, fin.Code));
            }));
        var aiClient = await CreateAdminClientAsync(aiFactory);
        var seed = await SeedAsync(aiClient);
        var payableId = await CreatePayableAsync(aiClient, seed);

        var res = await aiClient.PostAsync($"/api/payables/{payableId}/ai-classification-suggestion", null);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<AiSuggestionDto>();
        Assert.Equal("ai", body!.Source);
        Assert.Equal(acc.Id, body.AccountingAccountId);
        Assert.Equal(fin.Id, body.FinancialAccountId);
    }

    [Fact]
    public async Task Without_ai_falls_back_to_heuristic_source()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString); // default Fake provider
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var payableId = await CreatePayableAsync(client, seed);

        var res = await client.PostAsync($"/api/payables/{payableId}/ai-classification-suggestion", null);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<AiSuggestionDto>();
        Assert.Equal("heuristic", body!.Source);
    }
}
