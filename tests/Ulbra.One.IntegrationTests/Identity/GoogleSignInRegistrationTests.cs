using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ulbra.One.Identity.Application.Services;
using Ulbra.One.Identity.Domain; // Roles

namespace Ulbra.One.IntegrationTests.Identity;

/// <summary>
/// Google sign-in is invitation-only: an e-mail only gets in if an admin registered it first (or it is a
/// configured bootstrap admin). The pre-registered role/scope is what the first login carries, and the
/// registration itself is the authorization — a registered e-mail signs in even outside AllowedDomains.
/// </summary>
[Collection("Postgres")]
public class GoogleSignInRegistrationTests(PostgresFixture fixture)
{
    private const string BootstrapAdminEmail = "bootstrap-admin@parceiro.com";

    /// <summary>Stands in for Google: the "ID token" is just <c>email|name|picture</c>, already verified.</summary>
    private sealed class FakeGoogleTokenValidator : IGoogleTokenValidator
    {
        public Task<GoogleTokenPayload> ValidateAsync(string idToken, string expectedAudience, CancellationToken ct)
        {
            var parts = idToken.Split('|');
            return Task.FromResult(new GoogleTokenPayload(
                Subject: $"google-{parts[0]}",
                Email: parts[0],
                EmailVerified: true,
                Name: parts.Length > 1 ? parts[1] : "Google User",
                Picture: parts.Length > 2 && parts[2].Length > 0 ? parts[2] : null,
                HostedDomain: null));
        }
    }

    private sealed record TokenResponse(string Token);

    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    /// <summary>A host with Google sign-in configured and the token validation faked.</summary>
    private WebApplicationFactory<Program> GoogleFactory() =>
        new ApiFactory(fixture.ConnectionString).WithWebHostBuilder(b =>
        {
            b.UseSetting("Authentication:Google:ClientId", "test-client-id");
            b.UseSetting("Authentication:Google:BootstrapAdmins:0", BootstrapAdminEmail);
            b.ConfigureServices(s =>
            {
                s.RemoveAll<IGoogleTokenValidator>();
                s.AddScoped<IGoogleTokenValidator, FakeGoogleTokenValidator>();
            });
        });

    /// <summary>Authenticates an admin client from any WebApplicationFactory instance.</summary>
    private static async Task<HttpClient> CreateAdminClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/dev-token", new { email = "admin@ulbra.br", name = "Admin Teste" });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return client;
    }

    private static Task<HttpResponseMessage> SignInAsync(
        WebApplicationFactory<Program> factory, string email, string name = "Google User", string picture = "") =>
        factory.CreateClient().PostAsJsonAsync("/api/auth/google", new { idToken = $"{email}|{name}|{picture}" });

    [Fact]
    public async Task Unregistered_email_is_denied()
    {
        await using var factory = GoogleFactory();

        var res = await SignInAsync(factory, $"desconhecido-{Guid.NewGuid():N}@ulbra.br");

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("auth.user_not_registered", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Registered_email_signs_in_with_the_role_it_was_registered_with()
    {
        await using var factory = GoogleFactory();
        var admin = await CreateAdminClientAsync(factory);
        var email = $"convidado-{Guid.NewGuid():N}@ulbra.br";
        (await admin.PostAsJsonAsync("/api/users", new { email, name = "Convidado", role = Roles.Controllership }))
            .EnsureSuccessStatusCode();

        var res = await SignInAsync(factory, email, "Nome Do Google");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await ReadJson(res);
        Assert.Equal(Roles.Controllership, body.GetProperty("user").GetProperty("role").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("token").GetString()));

        // The login bound to the pre-registered row — no second user, and it is no longer pending.
        var rows = (await ReadJson(await admin.GetAsync("/api/users")))
            .EnumerateArray().Where(u => u.GetProperty("email").GetString() == email).ToList();
        Assert.False(Assert.Single(rows).GetProperty("pendingFirstLogin").GetBoolean());
    }

    [Fact]
    public async Task Registered_email_outside_the_allowed_domains_signs_in()
    {
        await using var factory = GoogleFactory();
        var admin = await CreateAdminClientAsync(factory);
        var email = $"consultor-{Guid.NewGuid():N}@gmail.com";
        (await admin.PostAsJsonAsync("/api/users", new { email, name = "Consultor", role = Roles.Finance }))
            .EnsureSuccessStatusCode();

        var res = await SignInAsync(factory, email);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(Roles.Finance, (await ReadJson(res)).GetProperty("user").GetProperty("role").GetString());
    }

    [Fact]
    public async Task Bootstrap_admin_signs_in_without_being_registered()
    {
        await using var factory = GoogleFactory();

        var res = await SignInAsync(factory, BootstrapAdminEmail);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(Roles.Admin, (await ReadJson(res)).GetProperty("user").GetProperty("role").GetString());
    }

    [Fact]
    public async Task Registered_but_inactive_email_is_denied()
    {
        await using var factory = GoogleFactory();
        var admin = await CreateAdminClientAsync(factory);
        var email = $"inativo-{Guid.NewGuid():N}@ulbra.br";
        var created = await ReadJson(await admin.PostAsJsonAsync("/api/users", new { email, role = Roles.User }));
        var id = created.GetProperty("id").GetString();
        (await admin.PutAsJsonAsync($"/api/users/{id}/active", new { active = false })).EnsureSuccessStatusCode();

        var res = await SignInAsync(factory, email);

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("auth.user_inactive", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Google_profile_picture_is_stored_and_returned_by_me()
    {
        await using var factory = GoogleFactory();
        var admin = await CreateAdminClientAsync(factory);
        var email = $"comfoto-{Guid.NewGuid():N}@ulbra.br";
        (await admin.PostAsJsonAsync("/api/users", new { email, name = "Com Foto", role = Roles.Finance }))
            .EnsureSuccessStatusCode();
        const string picture = "https://lh3.googleusercontent.com/a/foto-de-perfil";

        var signIn = await SignInAsync(factory, email, "Com Foto", picture);
        signIn.EnsureSuccessStatusCode();

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", (await ReadJson(signIn)).GetProperty("token").GetString());
        var me = await ReadJson(await client.GetAsync("/api/auth/me"));

        Assert.Equal(picture, me.GetProperty("pictureUrl").GetString());
    }

    /// <summary>The users admin screen carries the photo too — it is what names (and now pictures) the actor
    /// on the audit trail.</summary>
    [Fact]
    public async Task Google_profile_picture_is_listed_on_the_users_screen()
    {
        await using var factory = GoogleFactory();
        var admin = await CreateAdminClientAsync(factory);
        var email = $"listada-{Guid.NewGuid():N}@ulbra.br";
        (await admin.PostAsJsonAsync("/api/users", new { email, name = "Listada", role = Roles.Finance }))
            .EnsureSuccessStatusCode();
        const string picture = "https://lh3.googleusercontent.com/a/listada";

        (await SignInAsync(factory, email, "Listada", picture)).EnsureSuccessStatusCode();

        var row = (await ReadJson(await admin.GetAsync("/api/users")))
            .EnumerateArray().Single(u => u.GetProperty("email").GetString() == email);
        Assert.Equal(picture, row.GetProperty("pictureUrl").GetString());
    }

    /// <summary>A user who never signed in (or whose Google account has no photo) reports a null picture —
    /// the front falls back to initials rather than a broken image.</summary>
    [Fact]
    public async Task User_without_a_google_picture_reports_null()
    {
        await using var factory = GoogleFactory();
        var admin = await CreateAdminClientAsync(factory);
        var email = $"semfoto-{Guid.NewGuid():N}@ulbra.br";
        (await admin.PostAsJsonAsync("/api/users", new { email, name = "Sem Foto", role = Roles.Finance }))
            .EnsureSuccessStatusCode();

        (await SignInAsync(factory, email, "Sem Foto")).EnsureSuccessStatusCode();

        var row = (await ReadJson(await admin.GetAsync("/api/users")))
            .EnumerateArray().Single(u => u.GetProperty("email").GetString() == email);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("pictureUrl").ValueKind);
    }
}
