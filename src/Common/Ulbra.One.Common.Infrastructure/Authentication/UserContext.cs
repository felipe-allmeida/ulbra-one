using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Ulbra.One.Common.Infrastructure.Authentication;

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
}
