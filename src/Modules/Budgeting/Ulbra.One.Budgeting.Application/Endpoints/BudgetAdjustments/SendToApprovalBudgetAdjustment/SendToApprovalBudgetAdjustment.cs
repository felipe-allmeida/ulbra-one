using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Budgeting.Application.Contracts;
using Ulbra.One.Budgeting.Application.Messaging;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Budgeting.Application.Endpoints.BudgetAdjustments.SendToApprovalBudgetAdjustment;

/// <summary>Triagem forwards an adjustment awaiting triage to the approver.</summary>
public sealed class SendToApprovalBudgetAdjustment : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/send-to-approval", Handle)
            .WithName("SendBudgetAdjustmentToApproval").WithTags("Budgeting")
            .WithSummary("Forwards a budget adjustment to approval.")
            .WithDescription("Triage step: moves an adjustment that is awaiting triage on to the approver "
                + "and notifies them. Records a transition stamped with the caller. Any other source state "
                + "returns 409.")
            .RequirePermission(Permissions.BudgetAdjustmentsTriage)
            .Produces<BudgetAdjustmentResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(
        BudgetAdjustmentId id, UserContext user, IUnitOfWork uow, IPublishEndpoint publish, TimeProvider clock, CancellationToken ct)
    {
        var budgetAdjustment = await uow.Set<BudgetAdjustment>().Include(a => a.Transitions)
            .FirstOrDefaultAsync(a => a.Id == id.Value, ct);
        if (budgetAdjustment is null) return TypedResults.NotFound();

        try { budgetAdjustment.SendToApproval(Guid.Parse(user.UserId), clock.GetUtcNow()); }
        catch (BudgetAdjustmentException ex) { return ErrorResults.InvalidState(ex.Error.Description, ex.Error.Code); }

        await uow.AddAsync(budgetAdjustment.Transitions[^1], ct); // explicit Added (set-key child on a Modified parent)
        await budgetAdjustment.PublishLastTransition(publish, ct); // notify the approver (Notifications module)
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(BudgetAdjustmentResponse.From(budgetAdjustment));
    }
}
