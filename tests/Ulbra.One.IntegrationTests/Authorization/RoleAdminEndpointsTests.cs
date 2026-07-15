using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Authentication; // ITokenService, Permissions
using Ulbra.One.Identity.Domain; // Roles

namespace Ulbra.One.IntegrationTests.Authorization;

/// <summary>
/// Endpoints de administração de acessos (cargos → permissões), gated por <c>admin:users</c>.
/// Só Admin (bypass) acessa; edição reconcilia as claims do cargo.
/// </summary>
[Collection("Postgres")]
public class RoleAdminEndpointsTests(PostgresFixture fixture)
{
    private sealed record RoleDto(string Id, string Name, List<string> Permissions);

    [Fact]
    public async Task NonAdmin_cannot_list_roles()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var tokens = factory.Services.GetRequiredService<ITokenService>();
        // Cargo sem admin:users → 403.
        var token = tokens.GenerateToken(Guid.NewGuid().ToString(), Roles.Finance, "F", "f@ulbra.br",
            new[] { Permissions.PayablesRead });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var res = await client.GetAsync("/api/roles");

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Admin_lists_and_updates_role_permissions()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        var roles = await client.GetFromJsonAsync<List<RoleDto>>("/api/roles");
        Assert.NotNull(roles);
        var finance = roles!.Single(r => r.Name == "Finance");

        var res = await client.PutAsJsonAsync($"/api/roles/{finance.Id}/permissions",
            new { permissions = new[] { "payables:read", "payables:write" } });
        res.EnsureSuccessStatusCode();

        var after = await client.GetFromJsonAsync<List<RoleDto>>("/api/roles");
        var financeAfter = after!.Single(r => r.Name == "Finance");
        Assert.Equal(new[] { "payables:read", "payables:write" }, financeAfter.Permissions.OrderBy(p => p).ToArray());
    }

    [Fact]
    public async Task Admin_lists_permission_catalog()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        var permissions = await client.GetFromJsonAsync<List<string>>("/api/permissions");

        Assert.NotNull(permissions);
        Assert.Contains("payables:approve", permissions!);
        Assert.Contains("accounting:post", permissions!);
    }
}
