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

namespace Ulbra.One.Core.Application.Endpoints.CostCenters.ListCostCenters;

public sealed class ListCostCenters : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", Handle)
            .WithName("ListCostCenters")
            .WithTags("Core")
            .WithSummary("Lists cost centers.")
            .WithDescription("Cost centers (naturezas), ordered by code. A reference list — no paging. "
                + "Each row carries the units it is linked to and its annual budget, summed from the "
                + "active monthly cells. Like the unit list it returns everything by default, since it is "
                + "shared across modules; pass scoped=true to narrow it to the caller's scope.")
            .RequirePermission(Permissions.CoreRead)
            .Produces<List<CostCenterResponse>>(StatusCodes.Status200OK);
    }

    private static async Task<IResult> Handle(
        [SwaggerSchema("Company that owns the cost centers. Omit to list across every company.")]
        CompanyId? companyId,
        [SwaggerSchema("Narrow the list to the cost centers in the caller's scope, instead of every one. "
            + "Opt-in, same as on the unit list.")]
        bool? scoped,
        UserContext user,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var query = uow.ReadSet<CostCenter>();
        if (companyId is { } cid) query = query.Where(c => c.CompanyId == cid.Value);

        var links = uow.ReadSet<CostCenterUnit>();
        var budgets = uow.ReadSet<CostCenterUnitBudget>();
        var rows = await query.OrderBy(c => c.Code)
            .Select(c => new
            {
                CostCenter = c,
                UnitIds = links.Where(x => x.CostCenterId == c.Id).Select(x => x.UnitId).ToList(),
                AnnualBudget = budgets.Where(b => b.CostCenterId == c.Id && b.Active).Sum(b => b.MonthlyLimit),
            })
            .ToListAsync(ct);

        // Opt-in scoping (AEL-883): the requisition form passes ?scoped=true so a scoped requester only sees the
        // cost centers in its scope. Other callers omit it and see all — the endpoint is shared (CoreRead).
        if (scoped == true)
            rows = rows.Where(r => user.IsCostCenterInScope(r.CostCenter.CompanyId, r.CostCenter.Id)).ToList();

        var costCenters = rows.Select(r => CostCenterResponse.From(r.CostCenter, r.UnitIds, r.AnnualBudget)).ToList();
        return TypedResults.Ok(costCenters);
    }
}
