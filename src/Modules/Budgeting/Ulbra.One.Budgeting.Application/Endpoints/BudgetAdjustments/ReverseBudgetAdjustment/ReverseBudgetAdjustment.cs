using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Budgeting.Application.Contracts;
using Ulbra.One.Budgeting.Application.Dimensions;
using Ulbra.One.Budgeting.Application.Messaging;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Budgeting.Application.Endpoints.BudgetAdjustments.ReverseBudgetAdjustment;

/// <summary>Reverses an already-approved adjustment, recomposing the ceiling by inverting the arithmetic
/// (Approved → Reversed) in the same transaction. Restricted to approvers. The adjustment record stays
/// immutable — a reversal is a transition, not a hard delete.</summary>
public sealed class ReverseBudgetAdjustment : IEndpoint
{
    public sealed record Request(
        [property: SwaggerSchema("Why the adjustment is being reversed. Optional, but it is what shows up "
            + "in the transition history the requester reads.")]
        [property: SwaggerExample("Approved against the wrong cost center.")]
        string? Reason);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/reverse", Handle)
            .WithName("ReverseBudgetAdjustment").WithTags("Budgeting")
            .WithSummary("Reverses an approved budget adjustment.")
            .WithDescription("Recomposes the ceiling by inverting the original arithmetic (Approved → "
                + "Reversed) in a single transaction. The adjustment record stays immutable — a reversal is "
                + "a transition, not a delete. Restricted to approvers: beyond the role permission the "
                + "caller needs the per-user approval grant, read live (403); Admin and Presidency bypass "
                + "it. Reversing anything but an approved adjustment, or one whose reversal would drive a "
                + "cell negative, returns 409.")
            .RequirePermission(Permissions.BudgetAdjustmentsApprove)
            .Produces<BudgetAdjustmentResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(
        BudgetAdjustmentId id, Request req, UserContext user, IApprovalAuthority authority, ICostCenterBudgetMutator budgetMutator,
        IUnitOfWork uow, IPublishEndpoint publish, TimeProvider clock, CancellationToken ct)
    {
        var budgetAdjustment = await uow.Set<BudgetAdjustment>().Include(a => a.Transitions)
            .FirstOrDefaultAsync(a => a.Id == id.Value, ct);
        if (budgetAdjustment is null) return TypedResults.NotFound();

        // Reversal is an approver action → same per-user grant as approve (AEL-883, read live). Admin/Presidency bypass.
        var profile = await authority.GetAsync(Guid.Parse(user.UserId), ct);
        if (!user.IsAdmin && !user.IsPresident && profile?.CanApproveBudgetAdjustment != true)
            return ErrorResults.Forbidden("You are not authorized to approve budget adjustments.", ErrorCodes.BudgetAdjustment.ApproverNotAuthorized);

        try
        {
            budgetAdjustment.Reverse(Guid.Parse(user.UserId), clock.GetUtcNow(), req.Reason);
            await BudgetAdjustmentMutation.ReverseAsync(budgetMutator, budgetAdjustment, ct);
        }
        catch (BudgetAdjustmentException ex)
        {
            return ErrorResults.InvalidState(ex.Error.Description, ex.Error.Code);
        }

        await uow.AddAsync(budgetAdjustment.Transitions[^1], ct); // explicit Added (set-key child on a Modified parent)
        await budgetAdjustment.PublishLastTransition(publish, ct); // notify the requester (Notifications module)
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(BudgetAdjustmentResponse.From(budgetAdjustment));
    }
}
