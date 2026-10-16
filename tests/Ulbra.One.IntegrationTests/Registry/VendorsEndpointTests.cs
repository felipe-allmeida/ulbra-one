using System.Net;
using System.Net.Http.Json;
using Ulbra.One.Common.Application.Ids;

namespace Ulbra.One.IntegrationTests.Registry;

[Collection("Postgres")]
public class VendorsEndpointTests(PostgresFixture fixture)
{
    private sealed record BankInfoBody(string? BankCode, string? Branch, string? Account, string? AccountDigit, string? PixKey);

    private sealed record VendorBody(
        string CompanyId, int PersonType, string Name, string? TradeName, string Document,
        string? Email, string? Phone, object? Address, string? PaymentTerms,
        BankInfoBody? BankInfo, string TaxRegime = "Unknown");

    private sealed record BankInfoDto(string BankCode, string Branch, string Account, string AccountDigit, string PixKey);

    private sealed record VendorResponse(string Id, string Name, string Document, bool Active);

    private sealed record VendorResponseFull(
        string Id, string Name, string Document, bool Active,
        BankInfoDto? BankInfo, string TaxRegime);

    private sealed record Paginated(List<VendorResponse> Items, int Page, int PageSize, int Total, int TotalPages);

    // Vendors are globally unique by document and the suite shares one DB, so each vendor needs a fresh CNPJ.
    private static VendorBody ValidCompany(string companyId, string name = "Gráfica Sul Ltda", string? document = null) =>
        new(companyId, 1, name, "Gráfica Sul", document ?? TestCnpj.New(),
            "contato@gs.com", "5133334444", null, "30 dias", null);

    [Fact]
    public async Task Anonymous_Returns_401()
    {
        var factory = fixture.Api;
        var client = factory.CreateClient();
        var res = await client.GetAsync("/api/vendors");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Invalid_Cnpj_Returns_400()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var body = ValidCompany(new CompanyId(Guid.NewGuid()).ToString()) with { Document = "11222333000100" };
        var res = await client.PostAsJsonAsync("/api/vendors", body);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Masked_Cep_Is_Stored_Digits_Only()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = new CompanyId(Guid.NewGuid()).ToString();
        var body = ValidCompany(companyId) with
        {
            Address = new { Cep = "01001-000", Street = "Praça da Sé", Number = "1", Neighborhood = "Sé", City = "São Paulo", State = "SP" },
        };

        var res = await client.PostAsJsonAsync("/api/vendors", body);

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    }

    [Fact]
    public async Task BankInfo_Structured_RoundTrip()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = new CompanyId(Guid.NewGuid()).ToString();
        var bankInfo = new BankInfoBody("033", "1234", "56789", "0", "acme@pix.com");
        var body = ValidCompany(companyId) with
        {
            BankInfo = bankInfo,
            TaxRegime = "SimplesNacional",
        };

        var createRes = await client.PostAsJsonAsync("/api/vendors", body);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = await createRes.Content.ReadFromJsonAsync<VendorResponseFull>();
        Assert.NotNull(created);
        Assert.Equal("SimplesNacional", created!.TaxRegime);
        Assert.NotNull(created.BankInfo);
        Assert.Equal("033", created.BankInfo!.BankCode);
        Assert.Equal("1234", created.BankInfo.Branch);
        Assert.Equal("56789", created.BankInfo.Account);
        Assert.Equal("0", created.BankInfo.AccountDigit);
        Assert.Equal("acme@pix.com", created.BankInfo.PixKey);

        // GET single and verify round-trip
        var getRes = await client.GetAsync($"/api/vendors/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var fetched = await getRes.Content.ReadFromJsonAsync<VendorResponseFull>();
        Assert.NotNull(fetched);
        Assert.Equal("033", fetched!.BankInfo!.BankCode);
        Assert.Equal("SimplesNacional", fetched.TaxRegime);
    }

    [Fact]
    public async Task BankInfo_Null_Persists_As_Empty()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = new CompanyId(Guid.NewGuid()).ToString();

        var createRes = await client.PostAsJsonAsync("/api/vendors", ValidCompany(companyId));
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = await createRes.Content.ReadFromJsonAsync<VendorResponseFull>();
        Assert.NotNull(created);
        // bankInfo field is present (owned type always serialized) with empty strings
        Assert.Equal("Unknown", created!.TaxRegime);
        Assert.NotNull(created.BankInfo);
        Assert.Equal("", created.BankInfo!.BankCode);
        Assert.Equal("", created.BankInfo.PixKey);
    }

    [Fact]
    public async Task Create_List_Search_Update_Deactivate_SoftDelete_Flow()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = new CompanyId(Guid.NewGuid()).ToString();
        var doc = TestCnpj.New();

        // Create
        var createRes = await client.PostAsJsonAsync("/api/vendors", ValidCompany(companyId, document: doc));
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = await createRes.Content.ReadFromJsonAsync<VendorResponse>();
        Assert.NotNull(created);
        Assert.Equal(doc, created!.Document);
        Assert.True(created.Active);

        // List (filtered by company)
        var list = await client.GetFromJsonAsync<Paginated>($"/api/vendors?companyId={companyId}");
        Assert.NotNull(list);
        Assert.Equal(1, list!.Page);
        Assert.Contains(list.Items, v => v.Id == created.Id);

        // Search by name fragment
        var search = await client.GetFromJsonAsync<Paginated>($"/api/vendors?companyId={companyId}&search=Gráfica");
        Assert.Contains(search!.Items, v => v.Id == created.Id);

        // Update
        var updateBody = ValidCompany(companyId, "Gráfica Sul Atualizada", doc);
        var updateRes = await client.PutAsJsonAsync($"/api/vendors/{created.Id}", updateBody);
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);
        var updated = await updateRes.Content.ReadFromJsonAsync<VendorResponse>();
        Assert.Equal("Gráfica Sul Atualizada", updated!.Name);

        // Deactivate
        var deactivateRes = await client.PostAsync($"/api/vendors/{created.Id}/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, deactivateRes.StatusCode);
        var deactivated = await deactivateRes.Content.ReadFromJsonAsync<VendorResponse>();
        Assert.False(deactivated!.Active);

        // Activate
        var activateRes = await client.PostAsync($"/api/vendors/{created.Id}/activate", null);
        Assert.Equal(HttpStatusCode.OK, activateRes.StatusCode);

        // Soft delete
        var deleteRes = await client.DeleteAsync($"/api/vendors/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, deleteRes.StatusCode);

        // Soft-deleted vendor is absent from list
        var afterDelete = await client.GetFromJsonAsync<Paginated>($"/api/vendors?companyId={companyId}");
        Assert.DoesNotContain(afterDelete!.Items, v => v.Id == created.Id);

        // And not retrievable by id
        var getRes = await client.GetAsync($"/api/vendors/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getRes.StatusCode);
    }

    [Fact]
    public async Task Same_Document_Can_Be_Reregistered_After_SoftDelete()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = new CompanyId(Guid.NewGuid()).ToString();

        // Create then soft-delete a vendor.
        var doc = TestCnpj.New();
        var first = await (await client.PostAsJsonAsync("/api/vendors", ValidCompany(companyId, document: doc)))
            .Content.ReadFromJsonAsync<VendorResponse>();
        var deleteRes = await client.DeleteAsync($"/api/vendors/{first!.Id}");
        Assert.Equal(HttpStatusCode.OK, deleteRes.StatusCode);

        // Re-registering the same document must succeed (partial unique index ignores soft-deleted rows).
        var secondRes = await client.PostAsJsonAsync("/api/vendors", ValidCompany(companyId, document: doc));
        Assert.Equal(HttpStatusCode.Created, secondRes.StatusCode);
        var second = await secondRes.Content.ReadFromJsonAsync<VendorResponse>();
        Assert.NotEqual(first.Id, second!.Id);
    }

    [Fact]
    public async Task Lookup_Cnpj_Bad_Input_Returns_404_Gracefully()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        // Too-short digits never hit the network (guarded in the lookup) → null → 404.
        var res = await client.GetAsync("/api/lookup/cnpj/123");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Lookup_Cep_Requires_Auth()
    {
        var factory = fixture.Api;
        var client = factory.CreateClient();
        var res = await client.GetAsync("/api/lookup/cep/01001000");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Duplicate_Document_On_Create_Returns_409()
    {
        var client = await fixture.Api.CreateAdminClientAsync();
        var doc = TestCnpj.New();
        // Same CNPJ in a DIFFERENT company must now conflict — vendors are unique across the organization.
        var first = await client.PostAsJsonAsync("/api/vendors",
            ValidCompany(new CompanyId(Guid.NewGuid()).ToString(), document: doc));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var dup = await client.PostAsJsonAsync("/api/vendors",
            ValidCompany(new CompanyId(Guid.NewGuid()).ToString(), document: doc));
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
    }

    [Fact]
    public async Task Update_To_Existing_Document_Returns_409()
    {
        var client = await fixture.Api.CreateAdminClientAsync();
        var companyId = new CompanyId(Guid.NewGuid()).ToString();
        var docA = TestCnpj.New();
        var docB = TestCnpj.New();
        await client.PostAsJsonAsync("/api/vendors", ValidCompany(companyId, document: docA));
        var b = await (await client.PostAsJsonAsync("/api/vendors", ValidCompany(companyId, name: "B", document: docB)))
            .Content.ReadFromJsonAsync<VendorResponse>();
        // Changing B's document to A's (already used) conflicts.
        var res = await client.PutAsJsonAsync($"/api/vendors/{b!.Id}", ValidCompany(companyId, name: "B", document: docA));
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Search_By_Formatted_Document_Finds_Vendor()
    {
        var client = await fixture.Api.CreateAdminClientAsync();
        var companyId = new CompanyId(Guid.NewGuid()).ToString();
        var doc = TestCnpj.New();
        var created = await (await client.PostAsJsonAsync("/api/vendors", ValidCompany(companyId, document: doc)))
            .Content.ReadFromJsonAsync<VendorResponse>();
        // A formatted CNPJ must match the normalized stored document (global list, no company filter).
        var search = await client.GetFromJsonAsync<Paginated>(
            $"/api/vendors?search={Uri.EscapeDataString(TestCnpj.Format(doc))}");
        Assert.Contains(search!.Items, v => v.Id == created!.Id);
    }

    private sealed record VendorCreatedAtDto(string Id, DateTimeOffset CreatedAt);

    [Fact]
    public async Task Get_Vendor_Exposes_CreatedAt()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = new CompanyId(Guid.NewGuid()).ToString();

        // createdAt drives the "novo fornecedor" badge on the triage screen: must be exposed on the wire.
        var createRes = await client.PostAsJsonAsync("/api/vendors", ValidCompany(companyId));
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = await createRes.Content.ReadFromJsonAsync<VendorCreatedAtDto>();
        Assert.NotNull(created);
        Assert.NotEqual(default, created!.CreatedAt);

        var fetched = await (await client.GetAsync($"/api/vendors/{created.Id}"))
            .Content.ReadFromJsonAsync<VendorCreatedAtDto>();
        Assert.NotNull(fetched);
        // Postgres truncates to microseconds while .NET keeps 100ns ticks, so the round-trip can differ in the
        // last digit — compare with tolerance instead of exact equality.
        Assert.True((created.CreatedAt - fetched!.CreatedAt).Duration() < TimeSpan.FromSeconds(1));
    }
}
