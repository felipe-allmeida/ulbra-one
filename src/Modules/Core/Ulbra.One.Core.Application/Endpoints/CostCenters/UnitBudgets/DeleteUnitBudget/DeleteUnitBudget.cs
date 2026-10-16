using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Core.Application.Endpoints.CostCenters.UnitBudgets.DeleteUnitBudget;

/// <summary>Hard-deletes a single budget cell (frees the natural key for re-create/importer idempotency).</summary>
public sealed class DeleteUnitBudget : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapDelete("/{costCenterId}/unit-budgets/{budgetId}", Handle)
            .WithName("DeleteCostCenterUnitBudget")
            .WithTags("Core")
            .WithSummary("Deletes one monthly budget cell.")
            .WithDescription("Hard-deletes a single cell, freeing its (cost center, unit, competency) key "
                + "so the same month can be created again — which is what makes a re-run of the budget "
                + "importer idempotent. To keep the cell but stop it counting, deactivate it instead.")
            .RequirePermission(Permissions.CoreWrite)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        CostCenterId costCenterId,
        CostCenterUnitBudgetId budgetId,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var cell = await uow.Set<CostCenterUnitBudget>()
            .FirstOrDefaultAsync(b => b.Id == budgetId.Value && b.CostCenterId == costCenterId.Value, ct);
        if (cell is null) return TypedResults.NotFound();

        uow.Remove(cell);
        await uow.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }
}
