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

namespace Ulbra.One.Identity.Application.Endpoints.Users.SetUserRole;

/// <summary>Sets a user's role by name (must be an existing seeded role). Admin-only. Effective on the
/// user's next login (permissions/scope are denormalized into the JWT at login). AEL-883.</summary>
public sealed class SetUserRole : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPut("/{id}/role", Handle)
            .WithName("SetUserRole")
            .WithTags("Users")
            .WithSummary("Sets a user's role.")
            .WithDescription("Assigns one of the seeded roles by name. Permissions and scope are "
                + "denormalized into the JWT at sign-in, so the new role takes effect on the user's NEXT "
                + "login — an open session keeps the old permissions until its token expires.")
            .RequirePermission(Permissions.AdminUsers)
            .Produces<UserAdminResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(UserId id, SetUserRoleRequest req, IUnitOfWork uow, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Role))
            return ErrorResults.InvalidRequest("Role is required.", ErrorCodes.User.RoleInvalid);

        if (!await uow.ReadSet<Role>().AnyAsync(r => r.Name == req.Role, ct))
            return ErrorResults.InvalidRequest($"Role '{req.Role}' does not exist.", ErrorCodes.User.RoleInvalid);

        var user = await uow.Set<User>().FirstOrDefaultAsync(u => u.Id == id.Value, ct);
        if (user is null) return TypedResults.NotFound();

        user.SetRole(req.Role);
        await uow.SaveChangesAsync(ct);

        return TypedResults.Ok(await UserAdminResponseFactory.BuildAsync(uow, user, ct));
    }
}
