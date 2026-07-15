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

namespace Ulbra.One.Core.Application.Endpoints.CostCenters.ListCostCenters;

public sealed class ListCostCenters : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", Handle)
            .WithName("ListCostCenters")
            .WithTags("Core")
            .RequirePermission(Permissions.CoreRead)
            .Produces<List<CostCenterResponse>>(StatusCodes.Status200OK);
    }

    private static async Task<IResult> Handle(
        CompanyId? companyId,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var query = uow.ReadSet<CostCenter>();
        if (companyId is { } cid) query = query.Where(c => c.CompanyId == cid.Value);

        var links = uow.ReadSet<CostCenterUnit>();
        var rows = await query.OrderBy(c => c.Code)
            .Select(c => new
            {
                CostCenter = c,
                UnitIds = links.Where(x => x.CostCenterId == c.Id).Select(x => x.UnitId).ToList(),
            })
            .ToListAsync(ct);

        var costCenters = rows.Select(r => CostCenterResponse.From(r.CostCenter, r.UnitIds)).ToList();
        return TypedResults.Ok(costCenters);
    }
}
