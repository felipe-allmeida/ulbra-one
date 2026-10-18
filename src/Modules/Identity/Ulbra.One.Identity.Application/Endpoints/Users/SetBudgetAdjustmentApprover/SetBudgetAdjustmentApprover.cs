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

namespace Ulbra.One.Identity.Application.Endpoints.Users.SetBudgetAdjustmentApprover;

/// <summary>Grants/revokes a user's authority to approve budget adjustments (adequações). Admin-only. The role
/// Controladoria carries the budget-adjustments:approve permission, but only users flagged here may actually
/// approve/reverse — read live at approve time, so revocation is immediate. AEL-883.</summary>
public sealed class SetBudgetAdjustmentApprover : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPut("/{id}/adjustment-approver", Handle)
            .WithName("SetBudgetAdjustmentApprover")
            .WithTags("Users")
            .WithSummary("Grants or revokes budget-adjustment approval.")
            .WithDescription("A second gate on top of the role permission: Controladoria carries "
                + "budget-adjustments:approve, but only users flagged here may actually approve or reverse "
                + "an adjustment. Read live at approval time, so revoking it takes effect immediately "
                + "rather than on next login. Admin and Presidency bypass the flag.")
            .RequirePermission(Permissions.AdminUsers)
            .Produces<UserAdminResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(UserId id, SetBudgetAdjustmentApproverRequest req, IUnitOfWork uow, CancellationToken ct)
    {
        var user = await uow.Set<User>().FirstOrDefaultAsync(u => u.Id == id.Value, ct);
        if (user is null) return TypedResults.NotFound();

        user.SetCanApproveBudgetAdjustment(req.CanApprove);
        await uow.SaveChangesAsync(ct);

        return TypedResults.Ok(await UserAdminResponseFactory.BuildAsync(uow, user, ct));
    }
}
