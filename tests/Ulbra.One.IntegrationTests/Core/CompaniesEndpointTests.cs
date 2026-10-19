using System.Net;
using System.Net.Http.Json;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Identity.Domain;

namespace Ulbra.One.IntegrationTests.Core;

[Collection("Postgres")]
public class CompaniesEndpointTests(PostgresFixture fixture)
{
    private sealed record CompanyBody(string LegalName, string Cnpj);
    private sealed record CompanyDto(string Id, string LegalName, string Cnpj, bool Active);

    // Companies are globally unique by CNPJ and the suite shares one DB, so each needs a fresh CNPJ.
    private static CompanyBody NewCompany(string name = "Empresa Teste Ltda", string? cnpj = null) =>
        new(name, cnpj ?? TestCnpj.New());

    [Fact]
    public async Task Delete_Anonymous_Returns_401()
    {
        var client = fixture.Api.CreateClient();
        var res = await client.DeleteAsync($"/api/companies/{new CompanyId(Guid.NewGuid())}");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Create_List_Update_Deactivate_Activate_SoftDelete_Flow()
    {
        var client = await fixture.Api.CreateAdminClientAsync();
        var cnpj = TestCnpj.New();

        // Create
        var createRes = await client.PostAsJsonAsync("/api/companies", NewCompany("Empresa Alpha", cnpj));
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = await createRes.Content.ReadFromJsonAsync<CompanyDto>();
        Assert.NotNull(created);
        Assert.Equal(cnpj, created!.Cnpj);
        Assert.True(created.Active);

        // List contains it
        var list = await client.GetFromJsonAsync<List<CompanyDto>>("/api/companies");
        Assert.Contains(list!, c => c.Id == created.Id);

        // Update
        var updateRes = await client.PutAsJsonAsync($"/api/companies/{created.Id}", NewCompany("Empresa Alpha Atualizada", cnpj));
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);
        var updated = await updateRes.Content.ReadFromJsonAsync<CompanyDto>();
        Assert.Equal("Empresa Alpha Atualizada", updated!.LegalName);

        // Deactivate → Active flips to false
        var deactivateRes = await client.PostAsync($"/api/companies/{created.Id}/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, deactivateRes.StatusCode);
        var deactivated = await deactivateRes.Content.ReadFromJsonAsync<CompanyDto>();
        Assert.False(deactivated!.Active);

        // Inactive company still lists (only DeletedAt hides rows; Active=false does not)
        var listWithInactive = await client.GetFromJsonAsync<List<CompanyDto>>("/api/companies");
        Assert.Contains(listWithInactive!, c => c.Id == created.Id && !c.Active);

        // Activate → Active flips back to true
        var activateRes = await client.PostAsync($"/api/companies/{created.Id}/activate", null);
        Assert.Equal(HttpStatusCode.OK, activateRes.StatusCode);
        var activated = await activateRes.Content.ReadFromJsonAsync<CompanyDto>();
        Assert.True(activated!.Active);

        // Soft delete
        var deleteRes = await client.DeleteAsync($"/api/companies/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, deleteRes.StatusCode);

        // Soft-deleted company is absent from the list
        var afterDelete = await client.GetFromJsonAsync<List<CompanyDto>>("/api/companies");
        Assert.DoesNotContain(afterDelete!, c => c.Id == created.Id);

        // And writes against it 404 (query filter hides it)
        var updateAfterDelete = await client.PutAsJsonAsync($"/api/companies/{created.Id}", NewCompany("Zumbi", cnpj));
        Assert.Equal(HttpStatusCode.NotFound, updateAfterDelete.StatusCode);
    }

    [Fact]
    public async Task Same_Cnpj_Can_Be_Reregistered_After_SoftDelete()
    {
        var client = await fixture.Api.CreateAdminClientAsync();
        var cnpj = TestCnpj.New();

        // Create then soft-delete a company.
        var first = await (await client.PostAsJsonAsync("/api/companies", NewCompany("Primeira", cnpj)))
            .Content.ReadFromJsonAsync<CompanyDto>();
        var deleteRes = await client.DeleteAsync($"/api/companies/{first!.Id}");
        Assert.Equal(HttpStatusCode.OK, deleteRes.StatusCode);

        // Re-registering the same CNPJ must succeed (partial unique index ignores soft-deleted rows).
        var secondRes = await client.PostAsJsonAsync("/api/companies", NewCompany("Segunda", cnpj));
        Assert.Equal(HttpStatusCode.Created, secondRes.StatusCode);
        var second = await secondRes.Content.ReadFromJsonAsync<CompanyDto>();
        Assert.NotEqual(first.Id, second!.Id);
    }

    [Fact]
    public async Task Delete_Requires_Admin_While_CoreWrite_Only_Inactivates()
    {
        var admin = await fixture.Api.CreateAdminClientAsync();
        var created = await (await admin.PostAsJsonAsync("/api/companies", NewCompany()))
            .Content.ReadFromJsonAsync<CompanyDto>();

        // Controllership holds core:write → may deactivate/activate...
        var controller = fixture.Api.CreateClientWith(Roles.Controllership, Permissions.CoreWrite);
        var deactivateRes = await controller.PostAsync($"/api/companies/{created!.Id}/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, deactivateRes.StatusCode);
        var activateRes = await controller.PostAsync($"/api/companies/{created.Id}/activate", null);
        Assert.Equal(HttpStatusCode.OK, activateRes.StatusCode);

        // ...but NOT delete — that is reserved to Admin (.RequireAdmin()).
        var deleteRes = await controller.DeleteAsync($"/api/companies/{created.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, deleteRes.StatusCode);

        // Admin can delete it.
        var adminDelete = await admin.DeleteAsync($"/api/companies/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, adminDelete.StatusCode);
    }
}
