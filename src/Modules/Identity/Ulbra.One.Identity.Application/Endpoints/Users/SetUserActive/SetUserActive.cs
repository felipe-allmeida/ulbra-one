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

namespace Ulbra.One.Identity.Application.Endpoints.Users.SetUserActive;

/// <summary>Activates/deactivates a user. Admin-only. Inactive users are denied at login (GoogleSignIn /
/// DevToken reject token issuance); existing tokens expire within the JWT lifetime. AEL-883.</summary>
public sealed class SetUserActive : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPut("/{id}/active", Handle)
            .WithName("SetUserActive")
            .WithTags("Users")
            .WithSummary("Activates or deactivates a user.")
            .WithDescription("Deactivating blocks sign-in: both Google sign-in and the dev token refuse to "
                + "issue a token for the account. It does NOT revoke a token already issued — an open "
                + "session keeps working until that token expires, so this is not an immediate lockout.")
            .RequirePermission(Permissions.AdminUsers)
            .Produces<UserAdminResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(UserId id, SetUserActiveRequest req, IUnitOfWork uow, CancellationToken ct)
    {
        var user = await uow.Set<User>().FirstOrDefaultAsync(u => u.Id == id.Value, ct);
        if (user is null) return TypedResults.NotFound();

        user.SetActive(req.Active);
        await uow.SaveChangesAsync(ct);

        return TypedResults.Ok(await UserAdminResponseFactory.BuildAsync(uow, user, ct));
    }
}
