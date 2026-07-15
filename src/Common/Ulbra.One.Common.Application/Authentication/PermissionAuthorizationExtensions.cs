using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Ulbra.One.Common.Application.Authentication;

/// <summary>
/// Gating de endpoints por permissão. Substitui <c>.RequireAuthorization()</c> quando o endpoint
/// exige uma permissão específica (formato <c>recurso:ação</c>). Admin faz bypass — ver
/// <see cref="PermissionPolicyProvider"/>. Irmã de <c>.RequireAdmin()</c>.
/// </summary>
public static class PermissionAuthorizationExtensions
{
    public static RouteHandlerBuilder RequirePermission(this RouteHandlerBuilder builder, string permission) =>
        builder.RequireAuthorization(PermissionPolicyProvider.Prefix + permission);

    public static RouteGroupBuilder RequirePermission(this RouteGroupBuilder builder, string permission) =>
        builder.RequireAuthorization(PermissionPolicyProvider.Prefix + permission);
}
