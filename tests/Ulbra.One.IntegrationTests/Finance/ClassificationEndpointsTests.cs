using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ulbra.One.IntegrationTests.Finance;

[Collection("Postgres")]
public class ClassificationEndpointsTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private static string UniqueCode(string prefix) => $"{prefix}.{Random.Shared.Next(100_000, 999_999)}";

    private sealed record Seed(string CompanyId, string CostCenterId, string VendorId);

    private static async Task<Seed> SeedAsync(HttpClient client, decimal autoApprovalLimit = 0)
    {
        var cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        var companyRes = await client.PostAsJsonAsync("/api/companies", new { legalName = "ULBRA Classif", cnpj });
        companyRes.EnsureSuccessStatusCode();
        var companyId = (await ReadJson(companyRes)).GetProperty("id").GetString()!;

        var ccRes = await client.PostAsJsonAsync("/api/cost-centers",
            new { companyId, code = "CC1", name = "Marketing", annualBudget = 1_000_000m, autoApprovalLimit, unitIds = Array.Empty<string>() });
        ccRes.EnsureSuccessStatusCode();
        var costCenterId = (await ReadJson(ccRes)).GetProperty("id").GetString()!;

        var vendorRes = await client.PostAsJsonAsync("/api/vendors", new
        {
            companyId,
            personType = 1,
            name = "Fornecedor Classif Ltda",
            tradeName = "Fornecedor Classif",
            document = "11.222.333/0001-81",
            email = "contato@fc.com",
            phone = "5133334444",
            address = (object?)null,
            paymentTerms = "30 dias",
            bankInfo = (string?)null,
        });
        vendorRes.EnsureSuccessStatusCode();
        var vendorId = (await ReadJson(vendorRes)).GetProperty("id").GetString()!;

        return new Seed(companyId, costCenterId, vendorId);
    }

    private sealed record AccountRef(string Id, string Code, string Kind);

    /// <summary>Returns one synthetic and one analytical account id from the global ledger chart
    /// (auto-seeded on startup).</summary>
    private static async Task<(string SyntheticId, string AnalyticalId)> CreateAnalyticalAsync(HttpClient client)
    {
        var synthetic = await client.GetFromJsonAsync<List<AccountRef>>("/api/ledger-accounts?kind=Synthetic");
        var analytical = await client.GetFromJsonAsync<List<AccountRef>>("/api/ledger-accounts?kind=Analytical&activeOnly=true");
        return (synthetic!.First().Id, analytical!.First().Id);
    }

    private static async Task<string> CreatePayableAsync(HttpClient client, Seed seed, decimal amount, bool approve = true)
    {
        var createRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Para classificar",
            installments = new[] { new { dueDate = "2026-07-10", amount } },
            type = "Product", // determined product — required before a payable can schedule
        });
        createRes.EnsureSuccessStatusCode();
        var payableId = (await ReadJson(createRes)).GetProperty("id").GetString()!;

        if (approve)
        {
            var approveRes = await client.PostAsJsonAsync($"/api/payables/{payableId}/approve", new { });
            approveRes.EnsureSuccessStatusCode();
        }
        return payableId;
    }

    // ---------------------------------------------------------------------------
    // Classify
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Classify_With_Valid_Analytical_Account_Returns_200_And_Sets_Id()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var payableId = await CreatePayableAsync(client, seed, 1500m);
        var (_, accId) = await CreateAnalyticalAsync(client);

        var res = await client.PostAsJsonAsync($"/api/payables/{payableId}/classify",
            new { accountingAccountId = accId });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await ReadJson(await client.GetAsync($"/api/payables/{payableId}"));
        Assert.Equal(accId, body.GetProperty("accountingAccountId").GetString());
        Assert.True(body.GetProperty("isClassified").GetBoolean());
    }

    [Fact]
    public async Task Classify_With_Synthetic_Account_Returns_400_NotAnalytical()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var payableId = await CreatePayableAsync(client, seed, 1500m);
        var (syntheticAccId, _) = await CreateAnalyticalAsync(client);

        var res = await client.PostAsJsonAsync($"/api/payables/{payableId}/classify",
            new { accountingAccountId = syntheticAccId });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("LedgerAccount.NotPostable", (await ReadJson(res)).GetProperty("title").GetString());
    }

    // ---------------------------------------------------------------------------
    // Classify batch
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ClassifyBatch_Classifies_All_And_Returns_Count()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var pa = await CreatePayableAsync(client, seed, 1000m);
        var pb = await CreatePayableAsync(client, seed, 2000m);
        var (_, accId) = await CreateAnalyticalAsync(client);

        var res = await client.PostAsJsonAsync("/api/payables/classify-batch",
            new { payableIds = new[] { pa, pb }, accountingAccountId = accId });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(2, (await ReadJson(res)).GetProperty("classifiedCount").GetInt32());

        var bodyA = await ReadJson(await client.GetAsync($"/api/payables/{pa}"));
        Assert.True(bodyA.GetProperty("isClassified").GetBoolean());
        var bodyB = await ReadJson(await client.GetAsync($"/api/payables/{pb}"));
        Assert.True(bodyB.GetProperty("isClassified").GetBoolean());
    }

    // ---------------------------------------------------------------------------
    // Classification suggestion
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Suggestion_Returns_Accounts_From_Prior_Classified_Payable_Of_Same_Vendor()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var (_, accId) = await CreateAnalyticalAsync(client);

        var classified = await CreatePayableAsync(client, seed, 1000m);
        var classifyRes = await client.PostAsJsonAsync($"/api/payables/{classified}/classify",
            new { accountingAccountId = accId });
        classifyRes.EnsureSuccessStatusCode();

        var fresh = await CreatePayableAsync(client, seed, 2000m);
        var sugg = await ReadJson(await client.GetAsync($"/api/payables/{fresh}/classification-suggestion"));
        Assert.Equal(accId, sugg.GetProperty("accountingAccountId").GetString());
        Assert.Equal(classified, sugg.GetProperty("basedOnPayableId").GetString());
    }

    // ---------------------------------------------------------------------------
    // Gate (policy on)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Approve_Gate_Blocks_When_Policy_On_And_Unclassified_Then_Allows_After_Classify()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client, autoApprovalLimit: 1000m);
        var (_, accId) = await CreateAnalyticalAsync(client);

        var policyRes = await client.PutAsJsonAsync($"/api/classification-policies/{seed.CompanyId}",
            new { requireToApprove = true, requireToPay = false });
        Assert.Equal(HttpStatusCode.OK, policyRes.StatusCode);

        // Above auto-approval limit → stays PendingApproval, unclassified.
        var payableId = await CreatePayableAsync(client, seed, 9000m, approve: false);

        var blocked = await client.PostAsJsonAsync($"/api/payables/{payableId}/approve", new { });
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        Assert.Equal("Payable.NotClassified", (await ReadJson(blocked)).GetProperty("title").GetString());

        var classifyRes = await client.PostAsJsonAsync($"/api/payables/{payableId}/classify",
            new { accountingAccountId = accId });
        classifyRes.EnsureSuccessStatusCode();

        var allowed = await client.PostAsJsonAsync($"/api/payables/{payableId}/approve", new { });
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    // ---------------------------------------------------------------------------
    // List filter
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task List_With_Classified_False_Excludes_Classified_Payable()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var (_, accId) = await CreateAnalyticalAsync(client);

        var classified = await CreatePayableAsync(client, seed, 1000m);
        var unclassified = await CreatePayableAsync(client, seed, 2000m);
        (await client.PostAsJsonAsync($"/api/payables/{classified}/classify",
            new { accountingAccountId = accId })).EnsureSuccessStatusCode();

        var list = await ReadJson(await client.GetAsync($"/api/payables?companyId={seed.CompanyId}&classified=false&pageSize=100"));
        var payableIds = list.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("payableId").GetString()).ToList();
        Assert.Contains(unclassified, payableIds);
        Assert.DoesNotContain(classified, payableIds);
    }
}
