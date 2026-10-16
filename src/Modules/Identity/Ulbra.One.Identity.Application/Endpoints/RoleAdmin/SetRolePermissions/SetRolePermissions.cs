using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Identity.Application.Contracts;
using Ulbra.One.Identity.Domain.Entities;

namespace Ulbra.One.Identity.Application.Endpoints.RoleAdmin.SetRolePermissions;

public sealed class SetRolePermissions : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPut("/{id}/permissions", Handle)
            .WithName("SetRolePermissions")
            .WithTags("Roles")
            .WithSummary("Replaces a role's permissions.")
            .WithDescription("Sets the role's complete permission set — a permission left out of the list "
                + "is revoked. This affects every user holding the role, and since permissions are baked "
                + "into the JWT at sign-in, the change reaches them on their next login rather than "
                + "immediately.")
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
            return ErrorResults.InvalidRequest($"Permissões desconhecidas: {string.Join(", ", invalid)}", ErrorCodes.Roles.InvalidPermission);

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
