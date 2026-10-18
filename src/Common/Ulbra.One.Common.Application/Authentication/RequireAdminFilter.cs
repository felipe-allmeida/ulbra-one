using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Common.Application.Authentication;

internal sealed class RequireAdminFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var role = context.HttpContext.User.FindFirstValue(ClaimTypes.Role);
        if (role != "Admin")
        {
            return ErrorResults.Forbidden("Admin access required.", ErrorCodes.Auth.AdminOnly);
        }

        return await next(context);
    }
}

public static class AdminAuthorizationExtensions
{
    public static RouteHandlerBuilder RequireAdmin(this RouteHandlerBuilder builder) =>
        builder.RequireAuthorization().AddEndpointFilter<RequireAdminFilter>();
}
