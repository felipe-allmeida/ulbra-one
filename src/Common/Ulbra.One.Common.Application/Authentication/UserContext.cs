using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using PermissionCatalog = Ulbra.One.Common.Application.Authentication.Permissions;

namespace Ulbra.One.Common.Application.Authentication;

public class UserContext(IHttpContextAccessor httpContextAccessor)
{
    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public string UserId => User?.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("User is not authenticated");

    public string? UserName => User?.FindFirstValue(ClaimTypes.Name);
    public string? UserEmail => User?.FindFirstValue(ClaimTypes.Email);
    public string? Role => User?.FindFirstValue(ClaimTypes.Role);

    public bool IsAdmin => Role == "Admin";
    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    /// <summary>Permissões concedidas (claims <c>permission</c> do JWT). Admin não precisa listar — faz bypass.</summary>
    public IReadOnlyCollection<string> Permissions =>
        User?.FindAll(PermissionCatalog.ClaimType).Select(c => c.Value).ToArray() ?? [];

    /// <summary>True quando o usuário é Admin (bypass) ou possui a permissão informada.</summary>
    public bool HasPermission(string permission) =>
        IsAdmin || (User?.HasClaim(PermissionCatalog.ClaimType, permission) ?? false);
}
