using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ulbra.One.Common.Infrastructure.Authentication;

internal sealed class RequireAdminFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var role = context.HttpContext.User.FindFirstValue(ClaimTypes.Role);
        if (role != "Admin")
        {
            return TypedResults.Problem(
                detail: "Acesso restrito a administradores.",
                statusCode: StatusCodes.Status403Forbidden,
                title: "Forbidden");
        }

        return await next(context);
    }
}

public static class AdminAuthorizationExtensions
{
    public static RouteHandlerBuilder RequireAdmin(this RouteHandlerBuilder builder) =>
        builder.RequireAuthorization().AddEndpointFilter<RequireAdminFilter>();
}
