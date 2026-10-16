using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.IntegrationTests;
using Ulbra.One.Common.Application.Authentication; // Permissions
using Ulbra.One.Common.Application.Ids; // UserId
using Ulbra.One.Common.Application.Persistence; // IUnitOfWork
using Ulbra.One.Identity.Domain; // Roles
using Ulbra.One.Identity.Domain.Entities; // User

namespace Ulbra.One.IntegrationTests.Authorization;

/// <summary>
/// Admin CRUD for per-company access grants (AEL-764): only admin:users may list users and set which
/// companies a user can see; the PUT replaces the full set (grant/revoke by diff); GET reflects it.
/// </summary>
[Collection("Postgres")]
public class UserScopeGrantAdminTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    /// <summary>Seeds a non-admin user directly (there is no create-user endpoint) and returns its id + email.</summary>
    private static async Task<(Guid Id, string Email)> SeedUserAsync(ApiFactory factory, string role)
    {
        var email = $"scope-{Guid.NewGuid():N}@ulbra.br";
        using var scope = factory.Services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var user = new User(email, "Scope User", role);
        await uow.AddAsync(user, CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);
        return (user.Id, email);
    }

    /// <summary>Creates a company and returns its wire id (prefixed typed-id form, as the API emits it).</summary>
    private static async Task<string> SeedCompanyAsync(HttpClient admin)
    {
        var cnpj = TestDocuments.RandomCnpj();
        var res = await admin.PostAsJsonAsync("/api/companies", new { legalName = "ULBRA Grants", cnpj });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task List_users_anonymous_returns_401()
    {
        var res = await fixture.Api.CreateClient().GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task List_users_without_admin_permission_returns_403()
    {
        var client = fixture.Api.CreateClientWith(Roles.Finance, Permissions.PayablesRead);
        var res = await client.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Set_scope_grants_without_admin_permission_returns_403()
    {
        var client = fixture.Api.CreateClientWith(Roles.Finance, Permissions.PayablesRead);
        var res = await client.PutAsJsonAsync(
            $"/api/users/{new UserId(Guid.NewGuid())}/scope",
            new { companyIds = new[] { new CompanyId(Guid.NewGuid()).ToString() } });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Set_scope_grants_unknown_user_returns_404()
    {
        var admin = await fixture.Api.CreateAdminClientAsync();
        var res = await admin.PutAsJsonAsync(
            $"/api/users/{new UserId(Guid.NewGuid())}/scope", new { companyIds = Array.Empty<Guid>() });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Set_scope_grants_empty_company_returns_400()
    {
        var admin = await fixture.Api.CreateAdminClientAsync();
        var (userId, _) = await SeedUserAsync(fixture.Api, Roles.Finance);
        var res = await admin.PutAsJsonAsync(
            $"/api/users/{new UserId(userId)}/scope",
            new { companyIds = new[] { new CompanyId(Guid.Empty).ToString() } });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Set_and_list_scope_grants_round_trips()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync();
        var company = await SeedCompanyAsync(admin);
        var (userId, email) = await SeedUserAsync(factory, Roles.Finance);

        // Grant one company.
        var putRes = await admin.PutAsJsonAsync(
            $"/api/users/{new UserId(userId)}/scope", new { companyIds = new[] { company } });
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);
        var granted = (await ReadJson(putRes)).GetProperty("companyIds").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(new[] { company }, granted);

        // GET /users reflects the grant on the right row.
        var listRes = await admin.GetAsync("/api/users");
        listRes.EnsureSuccessStatusCode();
        var row = (await ReadJson(listRes)).EnumerateArray().Single(u => u.GetProperty("email").GetString() == email);
        Assert.Equal(new[] { company }, row.GetProperty("companyIds").EnumerateArray().Select(e => e.GetString()).ToList());

        // Revoke all (empty set) — the diff removes the grant.
        var clearRes = await admin.PutAsJsonAsync(
            $"/api/users/{new UserId(userId)}/scope", new { companyIds = Array.Empty<Guid>() });
        Assert.Equal(HttpStatusCode.OK, clearRes.StatusCode);
        Assert.Empty((await ReadJson(clearRes)).GetProperty("companyIds").EnumerateArray());

        var afterList = await admin.GetAsync("/api/users");
        var afterRow = (await ReadJson(afterList)).EnumerateArray().Single(u => u.GetProperty("email").GetString() == email);
        Assert.Empty(afterRow.GetProperty("companyIds").EnumerateArray());
    }

    // ---- Unified scope: companies + units + cost centers (AEL-883) --------------------------------

    [Fact]
    public async Task Set_scope_with_units_and_cost_centers_round_trips()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync();
        var company = await SeedCompanyAsync(admin);
        var (userId, _) = await SeedUserAsync(factory, Roles.Requester);
        var unit = new UnitId(Guid.NewGuid()).ToString();
        var costCenter = new CostCenterId(Guid.NewGuid()).ToString();

        var body = await ReadJson(await admin.PutAsJsonAsync($"/api/users/{new UserId(userId)}/scope", new
        {
            companyIds = new[] { company },
            units = new[] { new { companyId = company, unitId = unit } },
            costCenters = new[] { new { companyId = company, costCenterId = costCenter } },
        }));
        Assert.Equal(new[] { company }, body.GetProperty("companyIds").EnumerateArray().Select(e => e.GetString()).ToList());
        var unitRow = body.GetProperty("units").EnumerateArray().Single();
        Assert.Equal(company, unitRow.GetProperty("companyId").GetString());
        Assert.Equal(unit, unitRow.GetProperty("unitId").GetString());
        var ccRow = body.GetProperty("costCenters").EnumerateArray().Single();
        Assert.Equal(company, ccRow.GetProperty("companyId").GetString());
        Assert.Equal(costCenter, ccRow.GetProperty("costCenterId").GetString());
    }

    [Fact]
    public async Task Set_scope_unit_of_ungranted_company_returns_400()
    {
        var admin = await fixture.Api.CreateAdminClientAsync();
        var company = await SeedCompanyAsync(admin);
        var (userId, _) = await SeedUserAsync(fixture.Api, Roles.Requester);
        var otherCompany = new CompanyId(Guid.NewGuid()).ToString();

        // The unit's company is NOT in companyIds → mismatch (a unit can't be scoped without its company).
        var res = await admin.PutAsJsonAsync($"/api/users/{new UserId(userId)}/scope", new
        {
            companyIds = new[] { company },
            units = new[] { new { companyId = otherCompany, unitId = new UnitId(Guid.NewGuid()).ToString() } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(ErrorCodes.User.ScopeCompanyMismatch, (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Set_scope_same_unit_under_two_companies_returns_400()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync();
        var companyA = await SeedCompanyAsync(admin);
        var companyB = await SeedCompanyAsync(admin);
        var (userId, _) = await SeedUserAsync(factory, Roles.Requester);
        var unit = new UnitId(Guid.NewGuid()).ToString();

        // Both companies granted (so it's not a mismatch), but the same unit id is sent under both → a clean
        // 400 (a unit id is unique per user), never an unhandled 500 from a duplicate dictionary key.
        var res = await admin.PutAsJsonAsync($"/api/users/{new UserId(userId)}/scope", new
        {
            companyIds = new[] { companyA, companyB },
            units = new[]
            {
                new { companyId = companyA, unitId = unit },
                new { companyId = companyB, unitId = unit },
            },
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(ErrorCodes.User.ScopeInvalid, (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Revoking_a_company_drops_its_units_and_cost_centers()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync();
        var company = await SeedCompanyAsync(admin);
        var (userId, _) = await SeedUserAsync(factory, Roles.Requester);
        var unit = new UnitId(Guid.NewGuid()).ToString();
        var costCenter = new CostCenterId(Guid.NewGuid()).ToString();

        var granted = await ReadJson(await admin.PutAsJsonAsync($"/api/users/{new UserId(userId)}/scope", new
        {
            companyIds = new[] { company },
            units = new[] { new { companyId = company, unitId = unit } },
            costCenters = new[] { new { companyId = company, costCenterId = costCenter } },
        }));
        Assert.Single(granted.GetProperty("units").EnumerateArray());
        Assert.Single(granted.GetProperty("costCenters").EnumerateArray());

        // Revoke the company entirely → its units and cost centers must be dropped by the same diff.
        var cleared = await ReadJson(await admin.PutAsJsonAsync($"/api/users/{new UserId(userId)}/scope", new
        {
            companyIds = Array.Empty<string>(),
            units = Array.Empty<object>(),
            costCenters = Array.Empty<object>(),
        }));
        Assert.Empty(cleared.GetProperty("companyIds").EnumerateArray());
        Assert.Empty(cleared.GetProperty("units").EnumerateArray());
        Assert.Empty(cleared.GetProperty("costCenters").EnumerateArray());
    }

    // ---- Ativo/Inativo + role (AEL-883) -----------------------------------------------------------

    [Fact]
    public async Task Set_active_toggles_the_flag()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync();
        var (userId, _) = await SeedUserAsync(factory, Roles.Finance);

        var off = await ReadJson(await admin.PutAsJsonAsync($"/api/users/{new UserId(userId)}/active", new { active = false }));
        Assert.False(off.GetProperty("active").GetBoolean());
        var on = await ReadJson(await admin.PutAsJsonAsync($"/api/users/{new UserId(userId)}/active", new { active = true }));
        Assert.True(on.GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task Set_active_without_admin_returns_403()
    {
        var client = fixture.Api.CreateClientWith(Roles.Finance, Permissions.PayablesRead);
        var res = await client.PutAsJsonAsync($"/api/users/{new UserId(Guid.NewGuid())}/active", new { active = false });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Set_active_unknown_user_returns_404()
    {
        var admin = await fixture.Api.CreateAdminClientAsync();
        var res = await admin.PutAsJsonAsync($"/api/users/{new UserId(Guid.NewGuid())}/active", new { active = false });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Set_role_changes_the_role()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync();
        var (userId, _) = await SeedUserAsync(factory, Roles.Finance);
        var res = await ReadJson(await admin.PutAsJsonAsync($"/api/users/{new UserId(userId)}/role", new { role = Roles.Approver }));
        Assert.Equal(Roles.Approver, res.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Set_role_unknown_role_returns_400()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync();
        var (userId, _) = await SeedUserAsync(factory, Roles.Finance);
        var res = await admin.PutAsJsonAsync($"/api/users/{new UserId(userId)}/role", new { role = "Nonexistent" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("user.role_invalid", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Set_role_without_admin_returns_403()
    {
        var client = fixture.Api.CreateClientWith(Roles.Finance, Permissions.PayablesRead);
        var res = await client.PutAsJsonAsync($"/api/users/{new UserId(Guid.NewGuid())}/role", new { role = Roles.Approver });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    // ---- Presidency role (AEL-883): almost-Admin, minus user config ------------------------------

    [Fact]
    public async Task Presidency_role_seeds_all_permissions_except_admin_users()
    {
        var factory = fixture.Api;
        // Persist a Presidency user and log in via dev-token so the token carries the SEEDED role claims
        // (validates the migration seed end-to-end, not a hand-built token).
        var (_, email) = await SeedUserAsync(factory, Roles.Presidency);
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/dev-token", new { email });
        login.EnsureSuccessStatusCode();
        var token = (await ReadJson(login)).GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var me = await ReadJson(await client.GetAsync("/api/auth/me"));
        var perms = me.GetProperty("permissions").EnumerateArray().Select(e => e.GetString()).ToHashSet();
        Assert.False(me.GetProperty("isAdmin").GetBoolean()); // permission-driven, not the global bypass
        // A cross-module sample of functional permissions is present…
        Assert.Contains("budget-adjustments:approve", perms);
        Assert.Contains("payables:pay", perms);
        Assert.Contains("accounting:post", perms);
        Assert.Contains("audit:read", perms);
        // …but user/role administration is withheld.
        Assert.DoesNotContain("admin:users", perms);

        // End-to-end: the user-admin surface is denied.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task Controllership_role_triages_and_approves_adjustment_by_default()
    {
        var factory = fixture.Api;
        // Persist a Controladoria user + dev-token so the token carries the seeded role claims.
        var (_, email) = await SeedUserAsync(factory, Roles.Controllership);
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/dev-token", new { email });
        login.EnsureSuccessStatusCode();
        var token = (await ReadJson(login)).GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var perms = (await ReadJson(await client.GetAsync("/api/auth/me")))
            .GetProperty("permissions").EnumerateArray().Select(e => e.GetString()).ToHashSet();
        Assert.Contains("budget-adjustments:triage", perms);  // triages the adequação (new default, shared with Accounting)
        Assert.Contains("budget-adjustments:approve", perms); // and still approves
    }
}
