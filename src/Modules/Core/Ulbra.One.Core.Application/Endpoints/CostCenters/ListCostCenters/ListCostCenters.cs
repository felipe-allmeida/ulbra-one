using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Core.Application.Contracts;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Core.Application.Endpoints.CostCenters.ListCostCenters;

public sealed class ListCostCenters : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", Handle)
            .WithName("ListCostCenters")
            .WithTags("Core")
            .RequireAuthorization()
            .Produces<List<CostCenterResponse>>(StatusCodes.Status200OK);
    }

    private static async Task<IResult> Handle(
        Guid? companyId,
        [FromKeyedServices("core")] IUnitOfWork uow,
        CancellationToken ct)
    {
        var query = uow.ReadSet<CostCenter>();
        if (companyId is not null) query = query.Where(c => c.CompanyId == companyId);

        var links = uow.ReadSet<CostCenterUnit>();
        var costCenters = await query.OrderBy(c => c.Code)
            .Select(c => new CostCenterResponse(
                c.Id, c.CompanyId, c.Code, c.Name, c.AnnualBudget, c.AutoApprovalLimit, c.Active,
                links.Where(x => x.CostCenterId == c.Id).Select(x => x.UnitId).ToList()))
            .ToListAsync(ct);

        return TypedResults.Ok(costCenters);
    }
}
