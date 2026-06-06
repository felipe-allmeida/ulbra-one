using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
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
            .RequireAuthorization()
            .Produces<CostCenterResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        CostCenterId id,
        [FromKeyedServices("core")] IUnitOfWork uow,
        CancellationToken ct)
    {
        var costCenter = await uow.ReadSet<CostCenter>().FirstOrDefaultAsync(c => c.Id == id.Value, ct);
        if (costCenter is null) return Results.NotFound();
        var unitIds = await uow.ReadSet<CostCenterUnit>()
            .Where(x => x.CostCenterId == id.Value).Select(x => x.UnitId).ToArrayAsync(ct);
        return Results.Ok(CostCenterResponse.From(costCenter, unitIds));
    }
}
