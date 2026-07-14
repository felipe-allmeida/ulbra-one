using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Ulbra.One.Common.Application.Authentication;

/// <summary>
/// Cria policies de permissão sob demanda: para um policy name <c>perm:&lt;permissão&gt;</c> exige
/// um usuário autenticado que seja <c>Admin</c> (bypass global) OU que possua a claim
/// <see cref="Permissions.ClaimType"/> com aquele valor. Evita registrar N policies nomeadas à mão.
/// Policy names fora do prefixo caem no provider padrão do ASP.NET Core.
/// </summary>
public sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    /// <summary>Prefixo dos policy names dinâmicos gerados por <c>RequirePermission</c>.</summary>
    public const string Prefix = "perm:";

    private const string AdminRole = "Admin";

    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options) =>
        _fallback = new DefaultAuthorizationPolicyProvider(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(Prefix, StringComparison.Ordinal))
            return _fallback.GetPolicyAsync(policyName);

        var permission = policyName[Prefix.Length..];
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(ctx =>
                ctx.User.HasClaim(ClaimTypes.Role, AdminRole) ||
                ctx.User.HasClaim(Permissions.ClaimType, permission))
            .Build();

        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}
