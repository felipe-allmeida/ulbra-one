using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Ulbra.One.IntegrationTests.Authorization;

/// <summary>Inactive users are denied at login (AEL-883): the Active gate lives at token issuance. Exercises
/// the DevToken path end-to-end (deactivate → login denied → reactivate → login restored); GoogleSignIn shares
/// the identical gate.</summary>
[Collection("Postgres")]
public class InactiveLoginTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Inactive_user_is_denied_dev_token_login()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync();
        var email = $"inactive-{Guid.NewGuid():N}@ulbra.br";

        // First login provisions the user (Active by default) and mints a token.
        var first = await factory.CreateClient().PostAsJsonAsync("/api/auth/dev-token", new { email, name = "Alvo" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Admin finds and deactivates the user.
        var users = await ReadJson(await admin.GetAsync("/api/users"));
        var userId = users.EnumerateArray().Single(u => u.GetProperty("email").GetString() == email).GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/users/{userId}/active", new { active = false })).StatusCode);

        // Re-login is now denied with 403 Auth.UserInactive — the gate is at issuance.
        var denied = await factory.CreateClient().PostAsJsonAsync("/api/auth/dev-token", new { email, name = "Alvo" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("auth.user_inactive", (await ReadJson(denied)).GetProperty("error_code").GetString());

        // Reactivating restores login.
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/users/{userId}/active", new { active = true })).StatusCode);
        var restored = await factory.CreateClient().PostAsJsonAsync("/api/auth/dev-token", new { email, name = "Alvo" });
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
    }
}
