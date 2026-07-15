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

namespace Ulbra.One.Identity.Application.Endpoints.Roles.SetRolePermissions;

public sealed class SetRolePermissions : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPut("/{id}/permissions", Handle)
            .WithName("SetRolePermissions")
            .WithTags("Roles")
            .RequirePermission(Permissions.AdminUsers)
            .Produces<RoleResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(RoleId id, SetRolePermissionsRequest req, IUnitOfWork uow, CancellationToken ct)
    {
        var desired = req.Permissions.Distinct().ToHashSet();

        var invalid = desired.Where(p => !Permissions.All.Contains(p)).ToList();
        if (invalid.Count > 0)
            return TypedResults.Problem(
                detail: $"Permissões desconhecidas: {string.Join(", ", invalid)}",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Roles.InvalidPermission");

        var role = await uow.Set<Role>().Include(r => r.Claims).FirstOrDefaultAsync(r => r.Id == id.Value, ct);
        if (role is null) return TypedResults.NotFound();

        var current = role.Claims.Select(c => c.Permission).ToHashSet();
        foreach (var permission in desired.Except(current)) role.Grant(permission);
        foreach (var permission in current.Except(desired)) role.Revoke(permission);
        await uow.SaveChangesAsync(ct);

        return TypedResults.Ok(new RoleResponse(
            new RoleId(role.Id),
            role.Name,
            role.Claims.Select(c => c.Permission).OrderBy(p => p).ToList()));
    }
}
