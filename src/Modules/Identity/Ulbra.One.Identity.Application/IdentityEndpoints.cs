using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Ulbra.One.Common.Application.Extensions;
using Ulbra.One.Identity.Application.Endpoints.Auth.DevToken;
using Ulbra.One.Identity.Application.Endpoints.Auth.GoogleSignIn;
using Ulbra.One.Identity.Application.Endpoints.Auth.Me;

namespace Ulbra.One.Identity.Application;

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder builder)
    {
        var auth = builder.MapGroup("/auth");
        auth.MapEndpoint<GoogleSignIn>();
        auth.MapEndpoint<DevToken>();
        auth.MapEndpoint<Me>();
        return builder;
    }
}
