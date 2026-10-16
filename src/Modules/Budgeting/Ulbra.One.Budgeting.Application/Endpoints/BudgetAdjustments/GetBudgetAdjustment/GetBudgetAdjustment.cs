using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Budgeting.Application.Contracts;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Budgeting.Application.Endpoints.BudgetAdjustments.GetBudgetAdjustment;

public sealed class GetBudgetAdjustment : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("/{id}", Handle)
            .WithName("GetBudgetAdjustment")
            .WithTags("Budgeting")
            .WithSummary("Gets a budget adjustment.")
            .WithDescription("Returns a single adjustment by id, including its full transition history "
                + "(who moved it through triage, approval, cancellation or reversal, and when).")
            .RequirePermission(Permissions.BudgetAdjustmentsRead)
            .Produces<BudgetAdjustmentResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        BudgetAdjustmentId id,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var budgetAdjustment = await uow.ReadSet<BudgetAdjustment>()
            .Include(a => a.Transitions)
            .FirstOrDefaultAsync(a => a.Id == id.Value, ct);
        if (budgetAdjustment is null) return TypedResults.NotFound();
        return TypedResults.Ok(BudgetAdjustmentResponse.From(budgetAdjustment));
    }
}
