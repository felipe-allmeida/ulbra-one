using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ulbra.One.IntegrationTests.Characterization;

[Collection("Postgres")]
public class CurrentBehaviorTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private static int _cnpjCounter;

    /// <summary>Generates a unique, check-digit-valid CNPJ (avoids the unique-index collisions on a shared DB).</summary>
    private static string NewCnpj()
    {
        var seq = Interlocked.Increment(ref _cnpjCounter);
        var baseDigits = (10000000000000L + seq * 100L).ToString().Substring(0, 12)
            .Select(c => c - '0').ToArray();
        int Dv(int[] d, int[] w)
        {
            var sum = 0;
            for (var i = 0; i < w.Length; i++) sum += d[i] * w[i];
            var rest = sum % 11;
            return rest < 2 ? 0 : 11 - rest;
        }
        int[] w1 = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        int[] w2 = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        var d1 = Dv(baseDigits, w1);
        var with1 = baseDigits.Append(d1).ToArray();
        var d2 = Dv(with1, w2);
        return string.Concat(baseDigits) + d1 + d2;
    }

    [Fact]
    public async Task Auth_Me_Requires_Token()
    {
        var f = fixture.Api;
        var anon = f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/auth/me")).StatusCode);

        var admin = await f.CreateAdminClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Full_Chain_Company_Unit_CostCenter_Transaction()
    {
        var f = fixture.Api;
        var client = await f.CreateAdminClientAsync();

        // Company
        var empRes = await client.PostAsJsonAsync("/api/companies", new { legalName = "ACME SA", cnpj = "11222333000181" });
        Assert.Equal(HttpStatusCode.Created, empRes.StatusCode);
        var companyId = (await ReadJson(empRes)).GetProperty("id").GetString();

        // Unit
        var uniRes = await client.PostAsJsonAsync("/api/units", new { companyId, name = "Campus Centro", acronym = "CC" });
        Assert.Equal(HttpStatusCode.Created, uniRes.StatusCode);
        var unitId = (await ReadJson(uniRes)).GetProperty("id").GetString();

        // Cost center with auto-approval
        var ccRes = await client.PostAsJsonAsync("/api/cost-centers", new
        {
            companyId, code = "MKT", name = "Marketing",
            annualBudget = 100000m, autoApprovalLimit = 5000m, unitIds = new[] { unitId }
        });
        Assert.Equal(HttpStatusCode.Created, ccRes.StatusCode);
        var centroId = (await ReadJson(ccRes)).GetProperty("id").GetString();

        // Vendor (required for a payable)
        var venRes = await client.PostAsJsonAsync("/api/vendors", new
        {
            companyId, personType = "Company", name = "Google Brasil",
            document = TestCnpj.New(), address = (object?)null
        });
        Assert.Equal(HttpStatusCode.Created, venRes.StatusCode);
        var vendorId = (await ReadJson(venRes)).GetProperty("id").GetString();

        // Transaction under the auto-approval limit -> should be auto-approved
        var lanRes = await client.PostAsJsonAsync("/api/transactions", new
        {
            companyId, unitId, costCenterId = centroId, type = "Payable",
            description = "Anúncios", vendorId, amount = 3000m, dueDate = "2026-07-01"
        });
        Assert.Equal(HttpStatusCode.Created, lanRes.StatusCode);
        var lan = await ReadJson(lanRes);
        Assert.Equal("Approved", lan.GetProperty("status").GetString());
        Assert.Equal("Google Brasil", lan.GetProperty("counterpartyName").GetString());
        Assert.Equal(vendorId, lan.GetProperty("vendorId").GetString());

        // List by company returns it (paginated envelope)
        var list = await ReadJson(await client.GetAsync($"/api/transactions?companyId={companyId}"));
        Assert.True(list.GetProperty("items").GetArrayLength() >= 1);
        Assert.True(list.GetProperty("total").GetInt32() >= 1);
        Assert.Equal(1, list.GetProperty("page").GetInt32());
    }

    [Fact]
    public async Task Transaction_Above_Limit_Stays_Pending_Then_Approve_Then_Pay()
    {
        var f = fixture.Api;
        var client = await f.CreateAdminClientAsync();

        var companyId = (await ReadJson(await client.PostAsJsonAsync("/api/companies",
            new { legalName = "Beta SA", cnpj = "11444777000161" }))).GetProperty("id").GetString();
        var unitId = (await ReadJson(await client.PostAsJsonAsync("/api/units",
            new { companyId, name = "Sede", acronym = "SE" }))).GetProperty("id").GetString();
        var centroId = (await ReadJson(await client.PostAsJsonAsync("/api/cost-centers",
            new { companyId, code = "TI", name = "TI", annualBudget = 50000m, autoApprovalLimit = 0m, unitIds = new[] { unitId } })))
            .GetProperty("id").GetString();

        var vendorId = (await ReadJson(await client.PostAsJsonAsync("/api/vendors", new
        {
            companyId, personType = "Company", name = "AWS",
            document = TestCnpj.New(), address = (object?)null
        }))).GetProperty("id").GetString();

        var lanId = (await ReadJson(await client.PostAsJsonAsync("/api/transactions", new
        {
            companyId, unitId, costCenterId = centroId, type = "Payable",
            description = "Servidor", vendorId, amount = 20000m, dueDate = "2026-08-01"
        }))).GetProperty("id").GetString();

        Assert.Equal("Pending", (await ReadJson(await client.GetAsync($"/api/transactions/{lanId}"))).GetProperty("status").GetString());

        await client.PostAsync($"/api/transactions/{lanId}/approve", null);
        Assert.Equal("Approved", (await ReadJson(await client.GetAsync($"/api/transactions/{lanId}"))).GetProperty("status").GetString());

        await client.PostAsync($"/api/transactions/{lanId}/pay", null);
        Assert.Equal("Paid", (await ReadJson(await client.GetAsync($"/api/transactions/{lanId}"))).GetProperty("status").GetString());
    }

    /// <summary>Creates a company + unit + cost center (no auto-approval) and returns their ids.</summary>
    private static async Task<(string companyId, string unitId, string costCenterId)> SetupChainAsync(HttpClient client)
    {
        var companyId = (await ReadJson(await client.PostAsJsonAsync("/api/companies",
            new { legalName = "Chain SA", cnpj = NewCnpj() }))).GetProperty("id").GetString()!;
        var unitId = (await ReadJson(await client.PostAsJsonAsync("/api/units",
            new { companyId, name = "Sede", acronym = "SE" }))).GetProperty("id").GetString()!;
        var costCenterId = (await ReadJson(await client.PostAsJsonAsync("/api/cost-centers",
            new { companyId, code = "GEN", name = "Geral", annualBudget = 100000m, autoApprovalLimit = 0m, unitIds = new[] { unitId } })))
            .GetProperty("id").GetString()!;
        return (companyId, unitId, costCenterId);
    }

    private static async Task<string> CreateVendorAsync(HttpClient client) =>
        (await ReadJson(await client.PostAsJsonAsync("/api/vendors", new
        {
            personType = "Company", name = "Fornecedor X",
            document = TestCnpj.New(), address = (object?)null
        }))).GetProperty("id").GetString()!;

    private static async Task<string> CreateCustomerAsync(HttpClient client, string companyId) =>
        (await ReadJson(await client.PostAsJsonAsync("/api/customers", new
        {
            companyId, personType = "Company", name = "Cliente Y",
            document = TestCnpj.New(), address = (object?)null
        }))).GetProperty("id").GetString()!;

    [Fact]
    public async Task Payable_With_Valid_Vendor_Returns_Created_With_CounterpartyName()
    {
        var f = fixture.Api;
        var client = await f.CreateAdminClientAsync();
        var (companyId, unitId, costCenterId) = await SetupChainAsync(client);
        var vendorId = await CreateVendorAsync(client);

        var res = await client.PostAsJsonAsync("/api/transactions", new
        {
            companyId, unitId, costCenterId, type = "Payable",
            description = "Compra", vendorId, amount = 1000m, dueDate = "2026-09-01"
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var t = await ReadJson(res);
        Assert.Equal("Fornecedor X", t.GetProperty("counterpartyName").GetString());
        Assert.Equal(vendorId, t.GetProperty("vendorId").GetString());
    }

    [Fact]
    public async Task Payable_Without_Vendor_Returns_400_VendorRequired()
    {
        var f = fixture.Api;
        var client = await f.CreateAdminClientAsync();
        var (companyId, unitId, costCenterId) = await SetupChainAsync(client);

        var res = await client.PostAsJsonAsync("/api/transactions", new
        {
            companyId, unitId, costCenterId, type = "Payable",
            description = "Compra sem fornecedor", amount = 1000m, dueDate = "2026-09-01"
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("transaction.vendor_required", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Transaction_Accepts_A_Vendor_Registered_Under_Any_Company()
    {
        // Vendors are global to the organization: one registered while company B was active is usable on a
        // company-A transaction. (Was the old company-scoping bug — now the intended behavior.)
        var f = fixture.Api;
        var client = await f.CreateAdminClientAsync();
        var (companyA, unitA, ccA) = await SetupChainAsync(client);
        var (companyB, _, _) = await SetupChainAsync(client);
        var vendor = await CreateVendorAsync(client);

        var res = await client.PostAsJsonAsync("/api/transactions", new
        {
            companyId = companyA, unitId = unitA, costCenterId = ccA, type = "Payable",
            description = "Fornecedor global usado em outra empresa", vendorId = vendor, amount = 1000m, dueDate = "2026-09-01"
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    }

    [Fact]
    public async Task Transaction_With_Nonexistent_Vendor_Returns_400_VendorNotFound()
    {
        var f = fixture.Api;
        var client = await f.CreateAdminClientAsync();
        var (companyA, unitA, ccA) = await SetupChainAsync(client);

        var res = await client.PostAsJsonAsync("/api/transactions", new
        {
            companyId = companyA, unitId = unitA, costCenterId = ccA, type = "Payable",
            description = "Fornecedor inexistente", vendorId = $"vendor_{Guid.NewGuid():N}", amount = 1000m, dueDate = "2026-09-01"
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("vendor.not_found", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Receivable_With_Valid_Customer_Returns_Created()
    {
        var f = fixture.Api;
        var client = await f.CreateAdminClientAsync();
        var (companyId, unitId, costCenterId) = await SetupChainAsync(client);
        var customerId = await CreateCustomerAsync(client, companyId);

        var res = await client.PostAsJsonAsync("/api/transactions", new
        {
            companyId, unitId, costCenterId, type = "Receivable",
            description = "Mensalidade", customerId, amount = 500m, dueDate = "2026-09-01"
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var t = await ReadJson(res);
        Assert.Equal("Cliente Y", t.GetProperty("counterpartyName").GetString());
        Assert.Equal(customerId, t.GetProperty("customerId").GetString());
    }
}
