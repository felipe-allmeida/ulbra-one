using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Ulbra.One.Common.Application.Ai;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Infrastructure.Ai;
using Ulbra.One.Identity.Domain.Entities;

namespace Ulbra.One.IntegrationTests;

public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public ApiFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Host configuration has the highest precedence, so this overrides the value in
        // appsettings.Development.json (which Program reads eagerly at startup). Pointing at the
        // ephemeral test container keeps the suite isolated from the dev/compose database.
        builder.ConfigureHostConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = _connectionString,
                ["RemittanceSweep:Enabled"] = "false",
                // Point the bus at an unroutable broker so the (now suite-long) shared host never
                // delivers outbox messages to a real RabbitMQ. With a per-test host this was incidental
                // — each host was disposed within ~1s, before the QueryDelay=1s outbox loop delivered —
                // but one long-lived host would otherwise let consumers fire across tests whenever a
                // dev-compose broker is up locally, making report/withholding assertions nondeterministic.
                // CI has no broker, so this just makes local runs match CI. Tests that need consumer
                // behaviour drive it directly (ProcessAsync), never via the bus.
                ["MassTransit:Host"] = "unreachable-test-broker.invalid",
            });
        });
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        // Guarantee the test host NEVER calls a real AI provider, regardless of ambient env vars
        // (Ai__Provider / Ai__ApiKey). A real key + the auto-classify consumer (PayableReceivedConsumer)
        // would bill Anthropic on every payable the suite creates — and the escalating redelivery would
        // multiply it. Forcing the offline Fake at the DI level (after AddAi ran) can't be overridden by
        // configuration/environment precedence. Tests that need a specific AI behaviour re-stub IAiCompletion
        // in their own WithWebHostBuilder (applied after this), so this is a safe floor.
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAiCompletion>();
            services.AddScoped<IAiCompletion, FakeAiCompletion>();
        });
    }

    public async Task<HttpClient> CreateAdminClientAsync(string email = "admin@ulbra.br", string name = "Admin Teste")
    {
        var client = CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/dev-token", new { email, name });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return client;
    }

    /// <summary>Mints a token with the given role and explicit permissions (via <see cref="ITokenService"/>)
    /// so tests can exercise the authorization gate for a non-admin role.</summary>
    public HttpClient CreateClientWith(string role, params string[] permissions)
    {
        var tokens = Services.GetRequiredService<ITokenService>();
        var token = tokens.GenerateToken(
            userId: Guid.NewGuid().ToString(),
            role: role,
            userName: $"{role} Teste",
            userEmail: $"{role.ToLowerInvariant()}@ulbra.br",
            permissions: permissions);
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Mints a token with the given role, explicit permissions AND company scope claims (via
    /// <see cref="ITokenService"/>), so tests can exercise the per-resource authorization gate (AEL-764)
    /// for a user restricted to specific companies. No scope + non-admin = fail-closed.</summary>
    public HttpClient CreateClientWithScope(string role, Guid[] companies, params string[] permissions)
    {
        var tokens = Services.GetRequiredService<ITokenService>();
        var token = tokens.GenerateToken(
            userId: Guid.NewGuid().ToString(),
            role: role,
            userName: $"{role} Teste",
            userEmail: $"{role.ToLowerInvariant()}@ulbra.br",
            permissions: permissions,
            scopes: companies.Select(ScopeClaim.Encode));
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Mints a token with company scope, per-unit AND per-cost-center scope claims (AEL-883, unified
    /// intersection model), so tests can exercise the scoped requester/approver gate. Explicit lists — an empty
    /// dimension blocks: a scoped role needs the company, the unit AND the cost center all granted
    /// (<c>UserContext.CanAccessScope</c>).</summary>
    public HttpClient CreateClientWithUnitScope(
        string role,
        Guid[] companies,
        (Guid Company, Guid Unit)[] units,
        (Guid Company, Guid CostCenter)[] costCenters,
        params string[] permissions)
    {
        var tokens = Services.GetRequiredService<ITokenService>();
        var token = tokens.GenerateToken(
            userId: Guid.NewGuid().ToString(),
            role: role,
            userName: $"{role} Teste",
            userEmail: $"{role.ToLowerInvariant()}@ulbra.br",
            permissions: permissions,
            scopes: companies.Select(ScopeClaim.Encode),
            unitScopes: units.Select(u => UnitScopeClaim.Encode(u.Company, u.Unit)),
            costCenterScopes: costCenters.Select(c => CostCenterScopeClaim.Encode(c.Company, c.CostCenter)));
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Persists a real user row (so the scope-grant FKs resolve) and mints a token bound to its id,
    /// letting a test act as a specific scoped user whose live DB scope it also seeds via
    /// <see cref="SeedUserScopeAsync"/>. Needed because the Approver's approve/inbox reads scope LIVE from the
    /// DB (AEL-883), so a token-only client with no grant rows would be blocked.</summary>
    public async Task<(HttpClient Client, Guid UserId)> CreatePersistedUserClientAsync(string role, params string[] permissions)
    {
        Guid userId;
        using (var scope = Services.CreateScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var user = new User($"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@ulbra.br", $"{role} Teste", role);
            await uow.AddAsync(user, CancellationToken.None);
            await uow.SaveChangesAsync(CancellationToken.None);
            userId = user.Id;
        }

        var tokens = Services.GetRequiredService<ITokenService>();
        var token = tokens.GenerateToken(
            userId: userId.ToString(),
            role: role,
            userName: $"{role} Teste",
            userEmail: $"{role.ToLowerInvariant()}-{userId:N}@ulbra.br",
            permissions: permissions);
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, userId);
    }

    /// <summary>Seeds a scoped user's live DB scope (one company + unit + cost-center grant) so the Approver's
    /// intersection check (<c>IUserScopeDirectory</c>) covers that dimension. AEL-883.</summary>
    public async Task SeedUserScopeAsync(Guid userId, Guid companyId, Guid unitId, Guid costCenterId)
    {
        using var scope = Services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.AddAsync(new UserScopeGrant(userId, companyId), CancellationToken.None);
        await uow.AddAsync(new UserUnitScopeGrant(userId, companyId, unitId), CancellationToken.None);
        await uow.AddAsync(new UserCostCenterScopeGrant(userId, companyId, costCenterId), CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>Signs in through <c>/auth/dev-token</c> as a user the local seed already created, so the token
    /// carries the permissions the <c>role_claim</c> SEED grants that role — not a hand-picked list like
    /// <see cref="CreateClientWith"/>. That is the difference that lets a test cover the seeded grants
    /// themselves. Asserts the role that came back: an unknown e-mail makes dev-token provision an ADMIN,
    /// which would turn a permission test green for the wrong reason.</summary>
    public async Task<HttpClient> CreateSeededUserClientAsync(string email, string expectedRole)
    {
        var client = CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/dev-token", new { email });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<SeededLoginResponse>();
        if (!string.Equals(body!.User.Role, expectedRole, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Expected '{email}' to be the seeded '{expectedRole}', but dev-token returned '{body.User.Role}'. "
                + "An unknown e-mail is provisioned as Admin, so this would have passed for the wrong reason.");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.Token);
        return client;
    }

    private sealed record TokenResponse(string Token);

    private sealed record SeededLoginResponse(string Token, SeededUser User);

    private sealed record SeededUser(string Role);
}
