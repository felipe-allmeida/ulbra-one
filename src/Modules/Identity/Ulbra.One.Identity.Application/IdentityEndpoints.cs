using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Ulbra.One.Common.Application.Extensions;
using Ulbra.One.Identity.Application.Endpoints.Auth.DevToken;
using Ulbra.One.Identity.Application.Endpoints.Auth.GoogleSignIn;
using Ulbra.One.Identity.Application.Endpoints.Auth.Me;
using Ulbra.One.Identity.Application.Endpoints.Roles.ListPermissions;
using Ulbra.One.Identity.Application.Endpoints.Roles.ListRoles;
using Ulbra.One.Identity.Application.Endpoints.Roles.SetRolePermissions;

namespace Ulbra.One.Identity.Application;

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder builder)
    {
        var auth = builder.MapGroup("/auth");
        auth.MapEndpoint<GoogleSignIn>();
        auth.MapEndpoint<DevToken>();
        auth.MapEndpoint<Me>();

        // Administração de acessos (cargos → permissões). Gated por admin:users (só Admin, na prática).
        var roles = builder.MapGroup("/roles");
        roles.MapEndpoint<ListRoles>();
        roles.MapEndpoint<SetRolePermissions>();
        builder.MapGroup("/permissions").MapEndpoint<ListPermissions>();

        return builder;
    }
}
