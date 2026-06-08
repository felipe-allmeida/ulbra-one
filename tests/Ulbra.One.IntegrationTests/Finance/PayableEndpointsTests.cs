using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ulbra.One.IntegrationTests.Finance;

[Collection("Postgres")]
public class PayableEndpointsTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private sealed record Seed(string CompanyId, string CostCenterId, string VendorId);

    /// <summary>Seeds a company, a cost center (with the given auto-approval limit) and a vendor via the public API.</summary>
    private static async Task<Seed> SeedAsync(HttpClient client, decimal autoApprovalLimit = 0)
    {
        // Company CNPJ is stored as-is (max 14 chars) and is globally unique → use a fresh 14-digit value per seed.
        var cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        var companyRes = await client.PostAsJsonAsync("/api/companies",
            new { legalName = "ULBRA Teste", cnpj });
        companyRes.EnsureSuccessStatusCode();
        var companyId = (await ReadJson(companyRes)).GetProperty("id").GetString()!;

        var ccRes = await client.PostAsJsonAsync("/api/cost-centers",
            new { companyId, code = "CC1", name = "Marketing", annualBudget = 1_000_000m, autoApprovalLimit, unitIds = Array.Empty<string>() });
        ccRes.EnsureSuccessStatusCode();
        var costCenterId = (await ReadJson(ccRes)).GetProperty("id").GetString()!;

        var vendorRes = await client.PostAsJsonAsync("/api/vendors",
            new
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

    [Fact]
    public async Task Anonymous_Returns_401()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = factory.CreateClient();
        var res = await client.GetAsync("/api/payables");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Create_Parcelado_Generate_Returns_201_With_Three_Installments()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);

        var res = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Compra parcelada",
            generate = new { totalAmount = 90000m, count = 3, firstDueDate = "2026-07-10", intervalMonths = 1 },
        });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var body = await ReadJson(res);
        Assert.Equal(90000m, body.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(3, body.GetProperty("installments").GetArrayLength());
    }

    [Fact]
    public async Task Create_AVista_Explicit_Single_Installment_Returns_201()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);

        var res = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Compra à vista",
            installments = new[] { new { dueDate = "2026-07-10", amount = 1500m } },
        });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var body = await ReadJson(res);
        Assert.Equal(1500m, body.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(1, body.GetProperty("installments").GetArrayLength());
    }

    [Fact]
    public async Task Create_Invalid_Vendor_Returns_400_VendorInvalid()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);

        var res = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = $"vendor_{Guid.NewGuid():N}",
            costCenterId = seed.CostCenterId,
            description = "Fornecedor inválido",
            installments = new[] { new { dueDate = "2026-07-10", amount = 100m } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Payable.VendorInvalid", (await ReadJson(res)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Create_Without_Installments_Or_Generate_Returns_400_InstallmentsInvalid()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);

        var res = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Sem parcelas",
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Payable.InstallmentsInvalid", (await ReadJson(res)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task AutoApproval_Within_Limit_Is_Approved_Above_Is_PendingApproval()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client, autoApprovalLimit: 5000m);

        var within = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Dentro do limite",
            installments = new[] { new { dueDate = "2026-07-10", amount = 1000m } },
        });
        Assert.Equal(HttpStatusCode.Created, within.StatusCode);
        Assert.Equal("Approved", (await ReadJson(within)).GetProperty("status").GetString());

        var above = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Acima do limite",
            installments = new[] { new { dueDate = "2026-07-10", amount = 9000m } },
        });
        Assert.Equal(HttpStatusCode.Created, above.StatusCode);
        Assert.Equal("PendingApproval", (await ReadJson(above)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task List_Returns_One_Row_Per_Installment_Ordered_By_DueDate()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);

        var createRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Parcelado para listar",
            generate = new { totalAmount = 90000m, count = 3, firstDueDate = "2026-07-10", intervalMonths = 1 },
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var payableId = (await ReadJson(createRes)).GetProperty("id").GetString()!;

        var listRes = await client.GetAsync($"/api/payables?companyId={seed.CompanyId}&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var list = await ReadJson(listRes);

        var rows = list.GetProperty("items").EnumerateArray()
            .Where(r => r.GetProperty("payableId").GetString() == payableId)
            .ToList();
        Assert.Equal(3, rows.Count);

        // Each row exposes its sequence + the total installment count, ordered by due date.
        var dueDates = rows.Select(r => r.GetProperty("dueDate").GetString()!).ToList();
        Assert.Equal(dueDates.OrderBy(d => d).ToList(), dueDates);
        Assert.All(rows, r => Assert.Equal(3, r.GetProperty("installmentCount").GetInt32()));
        Assert.Equal(new[] { 1, 2, 3 }, rows.Select(r => r.GetProperty("sequence").GetInt32()).ToArray());
    }

    [Fact]
    public async Task Get_Returns_Payable_With_Installments()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);

        var createRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Para buscar",
            generate = new { totalAmount = 60000m, count = 2, firstDueDate = "2026-07-10", intervalMonths = 1 },
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var payableId = (await ReadJson(createRes)).GetProperty("id").GetString()!;

        var getRes = await client.GetAsync($"/api/payables/{payableId}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var body = await ReadJson(getRes);
        Assert.Equal(payableId, body.GetProperty("id").GetString());
        Assert.Equal(2, body.GetProperty("installments").GetArrayLength());

        var missing = await client.GetAsync($"/api/payables/payable_{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
