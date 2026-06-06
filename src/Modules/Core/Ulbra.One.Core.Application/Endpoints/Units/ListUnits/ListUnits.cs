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

namespace Ulbra.One.Core.Application.Endpoints.Units.ListUnits;

public sealed class ListUnits : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", Handle)
            .WithName("ListUnits")
            .WithTags("Core")
            .RequireAuthorization()
            .Produces<List<UnitResponse>>(StatusCodes.Status200OK);
    }

    private static async Task<IResult> Handle(
        CompanyId? companyId,
        [FromKeyedServices("core")] IUnitOfWork uow,
        CancellationToken ct)
    {
        var query = uow.ReadSet<Unit>();
        if (companyId is { } cid) query = query.Where(u => u.CompanyId == cid.Value);
        var units = await query.OrderBy(u => u.Name).ToListAsync(ct);
        return TypedResults.Ok(units.Select(UnitResponse.From).ToList());
    }
}
