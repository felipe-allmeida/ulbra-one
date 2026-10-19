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

namespace Ulbra.One.Identity.Application.Endpoints.Users.SetUserScope;

/// <summary>Replaces a user's full scope by diff — companies, units and cost centers (AEL-883). Explicit
/// lists only (no "empty = all"; the UI's "Selecionar todos" sends the current snapshot). Only meaningful for
/// scoped roles (Requester/Approver); for other roles the scope is ignored at enforcement time. A unit or
/// cost center whose company isn't granted is rejected (400). Admin-only. Takes effect on next login for the
/// Requester (JWT claims) and immediately for the Approver (read live).</summary>
public sealed class SetUserScope : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPut("/{id}/scope", Handle)
            .WithName("SetUserScope")
            .WithTags("Users")
            .WithSummary("Replaces a user's access scope.")
            .WithDescription("Sets the whole scope at once — companies, units and cost centers — by diff "
                + "against what the user has. The lists are explicit and fail-closed: an empty one grants "
                + "nothing rather than everything, and anything left out is revoked. A unit or cost center "
                + "whose company is not also granted returns 400. Only scoped roles (Requester, Approver) "
                + "are constrained by it; for the others it is stored and ignored. Timing differs by role: "
                + "the Requester's scope travels in the JWT, so it applies on next login, while the "
                + "Approver's is read live and applies immediately.")
            .RequirePermission(Permissions.AdminUsers)
            .Produces<UserAdminResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(UserId id, SetUserScopeRequest req, IUnitOfWork uow, CancellationToken ct)
    {
        if (UserScopeWriter.Validate(req, out var scope) is { } invalid) return invalid;

        var user = await uow.Set<User>().FirstOrDefaultAsync(u => u.Id == id.Value, ct);
        if (user is null) return TypedResults.NotFound();

        await UserScopeWriter.ApplyAsync(uow, user.Id, scope, ct);
        await uow.SaveChangesAsync(ct);

        return TypedResults.Ok(await UserAdminResponseFactory.BuildAsync(uow, user, ct));
    }
}
