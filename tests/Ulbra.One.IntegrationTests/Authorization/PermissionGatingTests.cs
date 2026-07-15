using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Authentication; // ITokenService, Permissions
using Ulbra.One.Identity.Domain; // Roles

namespace Ulbra.One.IntegrationTests.Authorization;

/// <summary>
/// Verifica a autorização por claim ponta a ponta: um cargo sem a permissão de um endpoint
/// recebe 403; Admin faz bypass. Minta o token direto via <see cref="ITokenService"/> com o
/// cargo/permissões desejados — a policy `perm:&lt;x&gt;` decide antes do handler, então não
/// depende de o recurso existir.
/// </summary>
[Collection("Postgres")]
public class PermissionGatingTests(PostgresFixture fixture)
{
    private static HttpClient ClientWith(ApiFactory factory, string role, params string[] permissions)
    {
        var tokens = factory.Services.GetRequiredService<ITokenService>();
        var token = tokens.GenerateToken(
            userId: Guid.NewGuid().ToString(),
            role: role,
            userName: $"{role} Teste",
            userEmail: $"{role.ToLowerInvariant()}@ulbra.br",
            permissions: permissions);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Finance_without_payables_approve_gets_403_on_approve()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        // Financeiro tem payables read/write/pay, mas NÃO approve (segregação de função).
        var client = ClientWith(factory, Roles.Finance, Permissions.PayablesRead, Permissions.PayablesWrite, Permissions.PayablesPay);

        var res = await client.PostAsync($"/api/payables/{Guid.NewGuid()}/approve", null);

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Accounting_without_payables_read_gets_403_on_list_payables()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        // Contábil não tem payables:read → não lista contas a pagar.
        var client = ClientWith(factory, Roles.Accounting, Permissions.AccountingRead, Permissions.AccountingWrite, Permissions.AccountingPost);

        var res = await client.GetAsync("/api/payables");

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Admin_bypasses_the_permission_gate()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        // Admin não precisa de claims de permissão — o bypass da policy passa pela gate.
        var client = ClientWith(factory, Roles.Admin);

        var res = await client.PostAsync($"/api/payables/{Guid.NewGuid()}/approve", null);

        // Passou da autorização (não é 403/401); o recurso inexistente resolve depois no handler.
        Assert.NotEqual(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
