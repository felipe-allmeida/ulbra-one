using Microsoft.AspNetCore.Routing;
using Ulbra.One.Identity.Application.Endpoints;

namespace Ulbra.One.Identity.Application;

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder builder)
    {
        new AuthEndpoint().Map(builder);
        return builder;
    }
}
