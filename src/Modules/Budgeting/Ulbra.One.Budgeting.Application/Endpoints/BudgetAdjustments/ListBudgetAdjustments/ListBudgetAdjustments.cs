using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Budgeting.Application.Contracts;
using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Pagination;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Budgeting.Application.Endpoints.BudgetAdjustments.ListBudgetAdjustments;

/// <summary>Query-string filters for the budget adjustment browse list. Bound via <c>[AsParameters]</c>.</summary>
public sealed record ListBudgetAdjustmentsRequest : PaginationQueryParameters
{
    [SwaggerSchema("Company that owns the adjustments.")]
    public CompanyId? CompanyId { get; set; }

    [SwaggerSchema("Unit (organizational branch) the adjustments belong to.")]
    public UnitId? UnitId { get; set; }

    [SwaggerSchema("Cost center whose ceiling the adjustments change.")]
    public CostCenterId? CostCenterId { get; set; }

    [SwaggerSchema("Approval-flow stage to keep.")]
    public BudgetAdjustmentStatus? Status { get; set; }

    [SwaggerSchema("Adjustment type: supplementation, reduction or transfer between months.")]
    public BudgetAdjustmentType? Type { get; set; }

    [SwaggerSchema("Accounting competency (`YYYY-MM`). Matches an adjustment that touches that month on "
        + "either side — the destination or, for a transfer, the source.")]
    [SwaggerExample("2026-08")]
    public string? Competency { get; set; }
}

public sealed class ListBudgetAdjustments : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", Handle)
            .WithName("ListBudgetAdjustments")
            .WithTags("Budgeting")
            .WithSummary("Lists budget adjustments.")
            .WithDescription("Paginated browse list of budget adjustments (adequações), ordered by company "
                + "then code, both descending. Every filter is optional and combines with AND.")
            .RequirePermission(Permissions.BudgetAdjustmentsRead)
            .Produces<PaginatedResponse<BudgetAdjustmentResponse>>(StatusCodes.Status200OK);
    }

    private static async Task<IResult> Handle(
        [AsParameters] ListBudgetAdjustmentsRequest req,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var query = uow.ReadSet<BudgetAdjustment>();
        if (req.CompanyId is { } cid) query = query.Where(a => a.CompanyId == cid.Value);
        if (req.UnitId is { } uid) query = query.Where(a => a.UnitId == uid.Value);
        if (req.CostCenterId is { } ccid) query = query.Where(a => a.CostCenterId == ccid.Value);
        if (req.Status is { } st) query = query.Where(a => a.Status == st);
        if (req.Type is { } tp) query = query.Where(a => a.Type == tp);

        // A competency filter matches an adjustment touching that month on either side (destination or,
        // for a transfer, the source).
        if (!string.IsNullOrWhiteSpace(req.Competency))
        {
            var competency = req.Competency;
            query = query.Where(a => a.Competency == competency || a.SourceCompetency == competency);
        }

        var page = await query
            .OrderByDescending(a => a.CompanyId).ThenByDescending(a => a.Code)
            .ToPaginatedAsync(req, ct);
        return TypedResults.Ok(page.Map(BudgetAdjustmentResponse.From));
    }
}
