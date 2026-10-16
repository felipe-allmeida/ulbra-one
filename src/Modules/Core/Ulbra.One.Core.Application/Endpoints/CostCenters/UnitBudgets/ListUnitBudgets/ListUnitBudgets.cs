using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Core.Application.Contracts;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Core.Application.Endpoints.CostCenters.UnitBudgets.ListUnitBudgets;

/// <summary>Query-string filters for a cost center's budget cells. Bound via <c>[AsParameters]</c>.</summary>
public sealed record ListUnitBudgetsRequest
{
    [SwaggerSchema("Restrict to one unit's cells. Omit to list every unit under the cost center.")]
    public UnitId? UnitId { get; set; }

    [SwaggerSchema("Keep cells from this competency onward (`YYYY-MM`), inclusive.")]
    [SwaggerExample("2026-01")]
    public string? CompetencyFrom { get; set; }

    [SwaggerSchema("Keep cells up to this competency (`YYYY-MM`), inclusive.")]
    [SwaggerExample("2026-12")]
    public string? CompetencyTo { get; set; }
}

/// <summary>Lists a cost center's monthly budget cells, optionally filtered by unit and/or competency range.</summary>
public sealed class ListUnitBudgets : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("/{costCenterId}/unit-budgets", Handle)
            .WithName("ListCostCenterUnitBudgets")
            .WithTags("Core")
            .WithSummary("Lists a cost center's monthly budget cells.")
            .WithDescription("One row per (unit, competency) under the cost center — the individual "
                + "ceilings that a requisition is checked against. Both inactive and active cells are "
                + "returned; only the active ones count toward a ceiling. A reference list, no paging.")
            .RequirePermission(Permissions.CoreRead)
            .Produces<List<CostCenterUnitBudgetResponse>>(StatusCodes.Status200OK);
    }

    private static async Task<IResult> Handle(
        CostCenterId costCenterId,
        [AsParameters] ListUnitBudgetsRequest req,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var (unitId, competencyFrom, competencyTo) = (req.UnitId, req.CompetencyFrom, req.CompetencyTo);
        var query = uow.ReadSet<CostCenterUnitBudget>().Where(b => b.CostCenterId == costCenterId.Value);
        if (unitId is { } uid) query = query.Where(b => b.UnitId == uid.Value);
        if (!string.IsNullOrWhiteSpace(competencyFrom)) query = query.Where(b => string.Compare(b.Competency, competencyFrom) >= 0);
        if (!string.IsNullOrWhiteSpace(competencyTo)) query = query.Where(b => string.Compare(b.Competency, competencyTo) <= 0);

        var entities = await query.OrderBy(b => b.UnitId).ThenBy(b => b.Competency).ToListAsync(ct);
        var cells = entities.Select(CostCenterUnitBudgetResponse.From).ToList();
        return TypedResults.Ok(cells);
    }
}
