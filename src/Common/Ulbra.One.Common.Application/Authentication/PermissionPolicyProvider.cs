using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Ulbra.One.Common.Application.Authentication;

/// <summary>
/// Cria policies de permissão sob demanda: para um policy name <c>perm:&lt;permissão&gt;</c> exige
/// um usuário autenticado que seja <c>Admin</c> (bypass global) OU que possua a claim
/// <see cref="Permissions.ClaimType"/> com aquele valor. Um policy name pode listar várias permissões
/// separadas por <c>|</c> (any-of, via <c>RequireAnyPermission</c>) — basta possuir qualquer uma. Evita
/// registrar N policies nomeadas à mão. Policy names fora do prefixo caem no provider padrão do ASP.NET Core.
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

        // A policy name may list several permissions separated by '|' (any-of). Single-permission names are just a
        // one-element split, so this stays backward compatible. Permissions carry their own ':' — only the leading
        // "perm:" prefix is stripped, never the separators inside a permission.
        var permissions = policyName[Prefix.Length..].Split('|', StringSplitOptions.RemoveEmptyEntries);
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(ctx =>
                ctx.User.HasClaim(ClaimTypes.Role, AdminRole) ||
                permissions.Any(p => ctx.User.HasClaim(Permissions.ClaimType, p)))
            .Build();

        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}
