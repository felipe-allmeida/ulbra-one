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

namespace Ulbra.One.Identity.Application.Endpoints.Users.SetApprovalAuthority;

/// <summary>Sets a user's approval authority (alçada + autonomy). Admin-only. The requisition approval flow
/// reads these values via <see cref="IApprovalAuthority"/>.</summary>
public sealed class SetApprovalAuthority : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPut("/{id}/approval-authority", Handle)
            .WithName("SetApprovalAuthority")
            .WithTags("Users")
            .WithSummary("Sets a user's approval limit.")
            .WithDescription("Sets the highest value the user may approve (alçada). It is a hard cap read "
                + "live by the budget-adjustment flow, with no override — an adjustment above the limit is "
                + "refused rather than escalated — so a change here takes effect immediately. Null means "
                + "unlimited.")
            .RequirePermission(Permissions.AdminUsers)
            .Produces<UserApprovalAuthorityResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> Handle(UserId id, SetApprovalAuthorityRequest req, IUnitOfWork uow, CancellationToken ct)
    {
        if (req.ApprovalLimit is < 0)
            return ErrorResults.InvalidRequest("ApprovalLimit cannot be negative.", ErrorCodes.User.ApprovalLimitInvalid);

        var user = await uow.Set<User>().FirstOrDefaultAsync(u => u.Id == id.Value, ct);
        if (user is null) return TypedResults.NotFound();

        user.SetApprovalAuthority(req.ApprovalLimit, req.HasApprovalAutonomy);
        await uow.SaveChangesAsync(ct);

        return TypedResults.Ok(new UserApprovalAuthorityResponse(
            new UserId(user.Id), user.Name, user.Email, user.ApprovalLimit, user.HasApprovalAutonomy));
    }
}
