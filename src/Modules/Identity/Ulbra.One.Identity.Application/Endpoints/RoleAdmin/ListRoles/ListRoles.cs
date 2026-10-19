using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Identity.Application.Contracts;
using Ulbra.One.Identity.Domain.Entities;

namespace Ulbra.One.Identity.Application.Endpoints.RoleAdmin.ListRoles;

public sealed class ListRoles : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", Handle)
            .WithName("ListRoles")
            .WithTags("Roles")
            .WithSummary("Lists roles and their permissions.")
            .WithDescription("Every seeded role with the permissions currently granted to it. Roles are "
                + "seeded, not created through the API — there is no create or delete here, only editing "
                + "which permissions a role carries.")
            .RequirePermission(Permissions.AdminUsers)
            .Produces<List<RoleResponse>>(StatusCodes.Status200OK);
    }

    private static async Task<IResult> Handle(IUnitOfWork uow, CancellationToken ct)
    {
        var roles = await uow.ReadSet<Role>()
            .OrderBy(r => r.Name)
            .Select(r => new RoleResponse(
                new RoleId(r.Id),
                r.Name,
                r.Claims.Select(c => c.Permission).OrderBy(p => p).ToList()))
            .ToListAsync(ct);
        return TypedResults.Ok(roles);
    }
}
