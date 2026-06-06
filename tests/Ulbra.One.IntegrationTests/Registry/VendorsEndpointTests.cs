using System.Net;
using System.Net.Http.Json;
using Ulbra.One.Common.Application.Ids;

namespace Ulbra.One.IntegrationTests.Registry;

[Collection("Postgres")]
public class VendorsEndpointTests(PostgresFixture fixture)
{
    private sealed record VendorBody(
        string CompanyId, int PersonType, string Name, string? TradeName, string Document,
        string? Email, string? Phone, object? Address, string? PaymentTerms, string? BankInfo);

    private sealed record VendorResponse(string Id, string Name, string Document, bool Active);

    private sealed record Paginated(List<VendorResponse> Items, int Page, int PageSize, int Total, int TotalPages);

    private static VendorBody ValidCompany(string companyId, string name = "Gráfica Sul Ltda") =>
        new(companyId, 1, name, "Gráfica Sul", "11.222.333/0001-81",
            "contato@gs.com", "5133334444", null, "30 dias", null);

    [Fact]
    public async Task Anonymous_Returns_401()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = factory.CreateClient();
        var res = await client.GetAsync("/api/vendors");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Invalid_Cnpj_Returns_400()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var body = ValidCompany(new CompanyId(Guid.NewGuid()).ToString()) with { Document = "11222333000100" };
        var res = await client.PostAsJsonAsync("/api/vendors", body);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Create_List_Search_Update_Deactivate_SoftDelete_Flow()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var companyId = new CompanyId(Guid.NewGuid()).ToString();

        // Create
        var createRes = await client.PostAsJsonAsync("/api/vendors", ValidCompany(companyId));
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = await createRes.Content.ReadFromJsonAsync<VendorResponse>();
        Assert.NotNull(created);
        Assert.Equal("11222333000181", created!.Document);
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
        var updateBody = ValidCompany(companyId, "Gráfica Sul Atualizada");
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
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var companyId = new CompanyId(Guid.NewGuid()).ToString();

        // Create then soft-delete a vendor.
        var first = await (await client.PostAsJsonAsync("/api/vendors", ValidCompany(companyId)))
            .Content.ReadFromJsonAsync<VendorResponse>();
        var deleteRes = await client.DeleteAsync($"/api/vendors/{first!.Id}");
        Assert.Equal(HttpStatusCode.OK, deleteRes.StatusCode);

        // Re-registering the same document in the same company must succeed (partial unique index).
        var secondRes = await client.PostAsJsonAsync("/api/vendors", ValidCompany(companyId));
        Assert.Equal(HttpStatusCode.Created, secondRes.StatusCode);
        var second = await secondRes.Content.ReadFromJsonAsync<VendorResponse>();
        Assert.NotEqual(first.Id, second!.Id);
    }

    [Fact]
    public async Task Lookup_Cnpj_Bad_Input_Returns_404_Gracefully()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        // Too-short digits never hit the network (guarded in the lookup) → null → 404.
        var res = await client.GetAsync("/api/lookup/cnpj/123");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Lookup_Cep_Requires_Auth()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = factory.CreateClient();
        var res = await client.GetAsync("/api/lookup/cep/01001000");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
