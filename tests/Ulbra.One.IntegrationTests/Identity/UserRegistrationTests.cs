using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ulbra.One.Common.Application.Authentication; // Permissions
using Ulbra.One.Identity.Domain; // Roles

namespace Ulbra.One.IntegrationTests.Identity;

/// <summary>
/// Pre-registration of users (convite por e-mail): an admin creates the user ahead of the first sign-in,
/// already with the right role and scope, so the first Google login lands on the intended access instead of
/// the default <c>User</c> role. Until that login happens the row is flagged <c>pendingFirstLogin</c>.
/// </summary>
[Collection("Postgres")]
public class UserRegistrationTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private static string NewEmail() => $"convite-{Guid.NewGuid():N}@ulbra.br";

    /// <summary>Creates a company and returns its wire id (prefixed typed-id form, as the API emits it).</summary>
    private static async Task<string> SeedCompanyAsync(HttpClient admin)
    {
        var res = await admin.PostAsJsonAsync("/api/companies", new { legalName = "ULBRA Convites", cnpj = TestCnpj.New() });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Registers_user_with_role_and_marks_it_pending_first_login()
    {
        var admin = await fixture.Api.CreateAdminClientAsync();
        var email = NewEmail();

        var res = await admin.PostAsJsonAsync("/api/users", new { email, name = "Fulano Convidado", role = Roles.Accounting });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var created = await ReadJson(res);
        Assert.Equal(email, created.GetProperty("email").GetString());
        Assert.Equal(Roles.Accounting, created.GetProperty("role").GetString());
        Assert.True(created.GetProperty("active").GetBoolean());
        Assert.True(created.GetProperty("pendingFirstLogin").GetBoolean());

        var listed = (await ReadJson(await admin.GetAsync("/api/users")))
            .EnumerateArray().Single(u => u.GetProperty("email").GetString() == email);
        Assert.Equal(Roles.Accounting, listed.GetProperty("role").GetString());
        Assert.True(listed.GetProperty("pendingFirstLogin").GetBoolean());
    }

    [Fact]
    public async Task Registers_user_with_scope_and_approval_authority()
    {
        var admin = await fixture.Api.CreateAdminClientAsync();
        var companyId = await SeedCompanyAsync(admin);
        var email = NewEmail();

        var res = await admin.PostAsJsonAsync("/api/users", new
        {
            email,
            name = "Aprovador Convidado",
            role = Roles.Approver,
            companyIds = new[] { companyId },
            approvalLimit = 5000m,
            canApproveBudgetAdjustment = true,
        });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var created = await ReadJson(res);
        Assert.Equal(companyId, Assert.Single(created.GetProperty("companyIds").EnumerateArray()).GetString());
        Assert.True(created.GetProperty("canApproveBudgetAdjustment").GetBoolean());
    }

    [Fact]
    public async Task Registers_user_normalizing_the_email_to_lowercase()
    {
        var admin = await fixture.Api.CreateAdminClientAsync();
        var email = NewEmail();

        var res = await admin.PostAsJsonAsync("/api/users", new { email = email.ToUpperInvariant(), role = Roles.User });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.Equal(email, (await ReadJson(res)).GetProperty("email").GetString());
    }

    [Fact]
    public async Task Scoping_a_unit_whose_company_was_not_granted_returns_400()
    {
        var admin = await fixture.Api.CreateAdminClientAsync();
        var companyId = await SeedCompanyAsync(admin);

        var res = await admin.PostAsJsonAsync("/api/users", new
        {
            email = NewEmail(),
            role = Roles.Requester,
            companyIds = Array.Empty<string>(),
            units = new[] { new { companyId, unitId = "unit_" + Guid.NewGuid().ToString("N") } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Registering_an_already_registered_email_returns_409()
    {
        var admin = await fixture.Api.CreateAdminClientAsync();
        var email = NewEmail();
        (await admin.PostAsJsonAsync("/api/users", new { email, role = Roles.User })).EnsureSuccessStatusCode();

        var res = await admin.PostAsJsonAsync("/api/users", new { email, role = Roles.Finance });

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("user.email_already_registered", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Registering_with_an_unknown_role_returns_400()
    {
        var admin = await fixture.Api.CreateAdminClientAsync();

        var res = await admin.PostAsJsonAsync("/api/users", new { email = NewEmail(), role = "Inexistente" });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("user.role_invalid", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Registering_with_a_malformed_email_returns_400()
    {
        var admin = await fixture.Api.CreateAdminClientAsync();

        var res = await admin.PostAsJsonAsync("/api/users", new { email = "sem-arroba", role = Roles.User });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("user.email_invalid", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Registering_without_admin_users_permission_returns_403()
    {
        var client = fixture.Api.CreateClientWith(Roles.Finance, Permissions.PayablesRead);

        var res = await client.PostAsJsonAsync("/api/users", new { email = NewEmail(), role = Roles.User });

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
