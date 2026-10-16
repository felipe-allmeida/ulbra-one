using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Core.Application.Contracts;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Core.Application.Endpoints.CostCenters.UnitBudgets.UpdateUnitBudget;

/// <summary>Updates a single budget cell's monthly ceiling and active flag. Dimensions are immutable.</summary>
public sealed class UpdateUnitBudget : IEndpoint
{
    public sealed record Request
    {
        [SwaggerSchema("Ceiling for this single month, in BRL.")]
        [SwaggerExample("15000.00")]
        public decimal MonthlyLimit { get; set; }

        [SwaggerSchema("Whether this cell counts toward the budget. Setting it false keeps the row but "
            + "removes it from the ceiling checks and from the annual total.")]
        public bool Active { get; set; } = true;
    }

    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPut("/{costCenterId}/unit-budgets/{budgetId}", Handle)
            .WithName("UpdateCostCenterUnitBudget")
            .WithTags("Core")
            .WithSummary("Updates one monthly budget cell.")
            .WithDescription("Changes a single cell's ceiling and active flag. Its dimensions — cost "
                + "center, unit and competency — are immutable: to move a budget to another month, delete "
                + "the cell and create it again.")
            .RequirePermission(Permissions.CoreWrite)
            .Produces<CostCenterUnitBudgetResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> Handle(
        CostCenterId costCenterId,
        CostCenterUnitBudgetId budgetId,
        Request req,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        if (req.MonthlyLimit < 0)
            return ErrorResults.InvalidRequest("MonthlyLimit cannot be negative.", ErrorCodes.CostCenterUnitBudget.LimitInvalid);

        var cell = await uow.Set<CostCenterUnitBudget>()
            .FirstOrDefaultAsync(b => b.Id == budgetId.Value && b.CostCenterId == costCenterId.Value, ct);
        if (cell is null) return TypedResults.NotFound();

        cell.Update(req.MonthlyLimit, req.Active);
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(CostCenterUnitBudgetResponse.From(cell));
    }
}
