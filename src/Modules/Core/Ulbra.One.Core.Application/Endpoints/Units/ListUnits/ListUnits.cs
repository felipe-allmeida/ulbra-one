using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
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
            .WithSummary("Lists units.")
            .WithDescription("Units (organizational branches), ordered by name. A reference list — no "
                + "paging. By default it returns every unit the company has, because it is shared by "
                + "Finance, Controladoria and Projetos; pass scoped=true to narrow it to what the caller "
                + "may actually act on.")
            .RequirePermission(Permissions.CoreRead)
            .Produces<List<UnitResponse>>(StatusCodes.Status200OK);
    }

    private static async Task<IResult> Handle(
        [SwaggerSchema("Company that owns the units. Omit to list across every company.")]
        CompanyId? companyId,
        [SwaggerSchema("Narrow the list to the units the caller is scoped to, instead of every unit. "
            + "Opt-in: the requisition form passes it so a unit-scoped requester only sees what it can "
            + "create for. Admin and broad company grants pass everything either way.")]
        bool? scoped,
        UserContext user,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var query = uow.ReadSet<Unit>();
        if (companyId is { } cid) query = query.Where(u => u.CompanyId == cid.Value);
        var units = await query.OrderBy(u => u.Name).ToListAsync(ct);

        // Opt-in unit scoping (AEL-883): the requisition form passes ?scoped=true so a unit-scoped requester
        // only sees units it can create for (Admin + broad company grants pass all). Other callers omit it
        // and keep seeing every unit — the endpoint is shared (CoreRead) by Finance/Controladoria/Projetos.
        if (scoped == true)
            units = units.Where(u => user.IsUnitInScope(u.CompanyId, u.Id)).ToList();

        return TypedResults.Ok(units.Select(UnitResponse.From).ToList());
    }
}
