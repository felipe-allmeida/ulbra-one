using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Core.Application.Contracts;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Core.Application.Endpoints.CostCenters.GetCostCenter;

public sealed class GetCostCenter : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("/{id}", Handle)
            .WithName("GetCostCenter")
            .WithTags("Core")
            .WithSummary("Gets a cost center.")
            .WithDescription("Returns a single cost center by id, with the units linked to it and its "
                + "annual budget summed from the active monthly cells.")
            .RequirePermission(Permissions.CoreRead)
            .Produces<CostCenterResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        CostCenterId id,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var costCenter = await uow.ReadSet<CostCenter>().FirstOrDefaultAsync(c => c.Id == id.Value, ct);
        if (costCenter is null) return TypedResults.NotFound();
        var unitIds = await uow.ReadSet<CostCenterUnit>()
            .Where(x => x.CostCenterId == id.Value).Select(x => x.UnitId).ToArrayAsync(ct);
        var annualBudget = await uow.ReadSet<CostCenterUnitBudget>()
            .Where(b => b.CostCenterId == id.Value && b.Active).SumAsync(b => (decimal?)b.MonthlyLimit, ct) ?? 0m;
        return TypedResults.Ok(CostCenterResponse.From(costCenter, unitIds, annualBudget));
    }
}
