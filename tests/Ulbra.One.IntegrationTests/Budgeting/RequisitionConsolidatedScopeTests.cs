using System.Net;
using System.Text.Json;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Identity.Domain;

namespace Ulbra.One.IntegrationTests.Budgeting;

/// <summary>
/// AEL-906: /requisitions accepts an omitted companyId (consolidated "Todas as empresas" — the Approver /
/// Controllership inboxes), but only for a consolidated role. Scoped roles (Requester/Approver) stay exempt
/// because the scope filter already restricts them; a plain non-scoped/non-consolidated token must not sweep
/// every company via an omitted companyId.
/// </summary>
[Collection("Postgres")]
public class RequisitionConsolidatedScopeTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Consolidated_is_forbidden_for_a_non_consolidated_non_scoped_role()
    {
        var client = fixture.Api.CreateClientWith(Roles.User, Permissions.RequisitionsRead);
        var res = await client.GetAsync("/api/requisitions"); // companyId omitted
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("requisition.consolidated_forbidden", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Consolidated_is_allowed_for_a_consolidated_role()
    {
        var client = await fixture.Api.CreateAdminClientAsync();
        var res = await client.GetAsync("/api/requisitions"); // companyId omitted → across every company
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Omitted_companyId_is_allowed_for_a_scoped_role_and_stays_within_its_scope()
    {
        // A scoped role with no grants is fail-closed (empty), but must NOT be 403'd by the consolidated guard.
        var client = fixture.Api.CreateClientWith(Roles.Requester, Permissions.RequisitionsRead);
        var res = await client.GetAsync("/api/requisitions"); // companyId omitted
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Empty((await ReadJson(res)).GetProperty("items").EnumerateArray());
    }
}
