using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;

namespace Ulbra.One.Identity.Application.Endpoints.RoleAdmin.ListPermissions;

public sealed class ListPermissions : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", Handle)
            .WithName("ListPermissions")
            .WithTags("Roles")
            .RequirePermission(Permissions.AdminUsers)
            .Produces<IReadOnlyList<string>>(StatusCodes.Status200OK);
    }

    // Catálogo estático de permissões conhecidas (formato recurso:ação).
    private static IResult Handle() => TypedResults.Ok(Permissions.All);
}
