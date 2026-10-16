using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Budgeting.Application.Contracts;
using Ulbra.One.Budgeting.Application.Messaging;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Budgeting.Application.Endpoints.BudgetAdjustments.CancelBudgetAdjustment;

/// <summary>Cancels an adjustment still in the approval flow (awaiting triage or approval). Available to the
/// requester (write), triagem or the approver; the domain validates the source state.</summary>
public sealed class CancelBudgetAdjustment : IEndpoint
{
    public sealed record Request(
        [property: SwaggerSchema("Why the adjustment is being cancelled. Optional, but it is what shows up "
            + "in the transition history the requester reads.")]
        [property: SwaggerExample("Superseded by adjustment #412.")]
        string? Reason);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/cancel", Handle)
            .WithName("CancelBudgetAdjustment").WithTags("Budgeting")
            .WithSummary("Cancels a budget adjustment.")
            .WithDescription("Cancels an adjustment still in the approval flow (awaiting triage or "
                + "approval) and notifies the requester. Open to the requester, triage or the approver — "
                + "any one of the write, triage or approve permissions will do, hence the 403 — and the "
                + "domain validates the source state (409).")
            .RequireAuthorization()
            .Produces<BudgetAdjustmentResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(
        BudgetAdjustmentId id, Request req, UserContext user, IUnitOfWork uow, IPublishEndpoint publish, TimeProvider clock, CancellationToken ct)
    {
        if (!user.HasPermission(Permissions.BudgetAdjustmentsWrite)
            && !user.HasPermission(Permissions.BudgetAdjustmentsTriage)
            && !user.HasPermission(Permissions.BudgetAdjustmentsApprove))
            return ErrorResults.Forbidden("Requires an adjustment write, triage or approval permission.", ErrorCodes.BudgetAdjustment.Forbidden);

        var budgetAdjustment = await uow.Set<BudgetAdjustment>().Include(a => a.Transitions)
            .FirstOrDefaultAsync(a => a.Id == id.Value, ct);
        if (budgetAdjustment is null) return TypedResults.NotFound();

        try { budgetAdjustment.Cancel(Guid.Parse(user.UserId), clock.GetUtcNow(), req.Reason); }
        catch (BudgetAdjustmentException ex) { return ErrorResults.InvalidState(ex.Error.Description, ex.Error.Code); }

        await uow.AddAsync(budgetAdjustment.Transitions[^1], ct); // explicit Added (set-key child on a Modified parent)
        await budgetAdjustment.PublishLastTransition(publish, ct); // notify the requester (Notifications module)
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(BudgetAdjustmentResponse.From(budgetAdjustment));
    }
}
