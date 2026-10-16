using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Ulbra.One.Common.Application.Extensions;
using Ulbra.One.Identity.Application.Endpoints.Auth.DevToken;
using Ulbra.One.Identity.Application.Endpoints.Auth.GoogleSignIn;
using Ulbra.One.Identity.Application.Endpoints.Auth.Me;
using Ulbra.One.Identity.Application.Endpoints.RoleAdmin.ListPermissions;
using Ulbra.One.Identity.Application.Endpoints.RoleAdmin.ListRoles;
using Ulbra.One.Identity.Application.Endpoints.RoleAdmin.SetRolePermissions;
using Ulbra.One.Identity.Application.Endpoints.Users.CreateUser;
using Ulbra.One.Identity.Application.Endpoints.Users.ListUsers;
using Ulbra.One.Identity.Application.Endpoints.Users.SetApprovalAuthority;
using Ulbra.One.Identity.Application.Endpoints.Users.SetBudgetAdjustmentApprover;
using Ulbra.One.Identity.Application.Endpoints.Users.SetUserActive;
using Ulbra.One.Identity.Application.Endpoints.Users.SetUserRole;
using Ulbra.One.Identity.Application.Endpoints.Users.SetUserScope;

namespace Ulbra.One.Identity.Application;

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder builder)
    {
        var auth = builder.MapGroup("/auth");
        auth.MapEndpoint<GoogleSignIn>();
        auth.MapEndpoint<DevToken>();
        auth.MapEndpoint<Me>();

        // Access administration (roles → permissions). Gated by admin:users (only Admin, in practice).
        var roles = builder.MapGroup("/roles");
        roles.MapEndpoint<ListRoles>();
        roles.MapEndpoint<SetRolePermissions>();
        builder.MapGroup("/permissions").MapEndpoint<ListPermissions>();

        // User administration (approval alçada + per-company access). Gated by admin:users.
        var users = builder.MapGroup("/users");
        users.MapEndpoint<ListUsers>();
        users.MapEndpoint<CreateUser>();
        users.MapEndpoint<SetApprovalAuthority>();
        users.MapEndpoint<SetUserRole>();
        users.MapEndpoint<SetUserScope>();
        users.MapEndpoint<SetUserActive>();
        users.MapEndpoint<SetBudgetAdjustmentApprover>();

        return builder;
    }
}
