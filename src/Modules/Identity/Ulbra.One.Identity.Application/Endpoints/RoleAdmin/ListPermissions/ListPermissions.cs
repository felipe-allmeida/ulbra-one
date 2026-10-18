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
            .WithSummary("Lists every known permission.")
            .WithDescription("The static catalogue of permissions in `resource:action` form. It is "
                + "compiled into the API, not stored, so it is the authoritative set of values accepted by "
                + "PUT /api/roles/{id}/permissions — the access-admin screen builds its checkboxes from it.")
            .RequirePermission(Permissions.AdminUsers)
            .Produces<IReadOnlyList<string>>(StatusCodes.Status200OK);
    }

    // Catálogo estático de permissões conhecidas (formato recurso:ação).
    private static IResult Handle() => TypedResults.Ok(Permissions.All);
}
