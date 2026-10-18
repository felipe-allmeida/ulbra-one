using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Budgeting.Application.Contracts;
using Ulbra.One.Budgeting.Application.Dimensions;
using Ulbra.One.Budgeting.Application.Messaging;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Budgeting.Application.Endpoints.BudgetAdjustments.ApproveBudgetAdjustment;

/// <summary>Final approval of a budget adjustment — moves the budget. The approver's authorization limit
/// (alçada) is a hard domain rule with no override (409 when the value exceeds it; <c>null</c> = unlimited).
/// The ceiling mutation on the affected budget cell(s) commits in the SAME transaction as
/// the status change: Suplementação/Transfer create the destination cell if absent; Redução and the transfer
/// source require an existing cell and never go below zero.</summary>
public sealed class ApproveBudgetAdjustment : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/approve", Handle)
            .WithName("ApproveBudgetAdjustment").WithTags("Budgeting")
            .WithSummary("Approves a budget adjustment and moves the budget.")
            .WithDescription("Final approval — this is the step that actually changes the ceiling. The "
                + "budget mutation commits in the same transaction as the status change: Supplementation "
                + "and Transfer create the destination cell when absent, while Reduction and the transfer "
                + "source require an existing cell and never go below zero (409). Beyond the role "
                + "permission, the caller needs the per-user approval grant, read live so revocation is "
                + "immediate (403); Admin and Presidency bypass it. The approver's authorization limit "
                + "(alçada) is a hard rule with no override — a value above it returns 409.")
            .RequirePermission(Permissions.BudgetAdjustmentsApprove)
            .Produces<BudgetAdjustmentResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(
        BudgetAdjustmentId id, UserContext user, IApprovalAuthority authority, ICostCenterBudgetMutator budgetMutator,
        IUnitOfWork uow, IPublishEndpoint publish, TimeProvider clock, CancellationToken ct)
    {
        var budgetAdjustment = await uow.Set<BudgetAdjustment>().Include(a => a.Transitions)
            .FirstOrDefaultAsync(a => a.Id == id.Value, ct);
        if (budgetAdjustment is null) return TypedResults.NotFound();

        var actor = Guid.Parse(user.UserId);
        var profile = await authority.GetAsync(actor, ct);

        // Per-user approval grant (AEL-883): the Controladoria role carries budget-adjustments:approve, but only
        // users explicitly enabled in the Users admin screen may approve — read live (revocation is immediate).
        // Admin/Presidency bypass. The alçada (profile.ApprovalLimit) is a separate value cap enforced by the domain below.
        if (!user.IsAdmin && !user.IsPresident && profile?.CanApproveBudgetAdjustment != true)
            return ErrorResults.Forbidden("You are not authorized to approve budget adjustments.", ErrorCodes.BudgetAdjustment.ApproverNotAuthorized);

        try
        {
            budgetAdjustment.Approve(actor, clock.GetUtcNow(), profile?.ApprovalLimit);
            await BudgetAdjustmentMutation.ApplyAsync(budgetMutator, budgetAdjustment, ct);
        }
        catch (BudgetAdjustmentException ex)
        {
            // Alçada exceeded, invalid transition, missing cell to reduce, or a reduction that would go
            // negative — nothing is saved, so the aggregate + budget mutation are discarded together.
            return ErrorResults.InvalidState(ex.Error.Description, ex.Error.Code);
        }

        await uow.AddAsync(budgetAdjustment.Transitions[^1], ct); // explicit Added (set-key child on a Modified parent)
        await budgetAdjustment.PublishLastTransition(publish, ct); // notify the requester (Notifications module)
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(BudgetAdjustmentResponse.From(budgetAdjustment));
    }
}
