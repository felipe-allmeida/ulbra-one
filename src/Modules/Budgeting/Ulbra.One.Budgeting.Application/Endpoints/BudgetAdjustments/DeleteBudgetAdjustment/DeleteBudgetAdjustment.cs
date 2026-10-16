using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Budgeting.Application.Endpoints.BudgetAdjustments.DeleteBudgetAdjustment;

/// <summary>Hard-deletes an adjustment still awaiting triage (discarding an unsent request). Once it has
/// advanced it is cancelled (a transition), never deleted — so a non-triage state returns 409.</summary>
public sealed class DeleteBudgetAdjustment : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapDelete("/{id}", Handle)
            .WithName("DeleteBudgetAdjustment")
            .WithTags("Budgeting")
            .WithSummary("Deletes a budget adjustment.")
            .WithDescription("Hard-deletes an adjustment still awaiting triage — discarding a request that "
                + "was never sent. Once it has advanced it is cancelled through a transition rather than "
                + "deleted, so any other state returns 409.")
            .RequirePermission(Permissions.BudgetAdjustmentsWrite)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> Handle(
        BudgetAdjustmentId id,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var budgetAdjustment = await uow.Set<BudgetAdjustment>().FirstOrDefaultAsync(a => a.Id == id.Value, ct);
        if (budgetAdjustment is null) return TypedResults.NotFound();

        try
        {
            budgetAdjustment.EnsureDeletable();
        }
        catch (BudgetAdjustmentException ex)
        {
            return ErrorResults.InvalidState(ex.Error.Description, ex.Error.Code);
        }

        uow.Remove(budgetAdjustment);
        await uow.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }
}
