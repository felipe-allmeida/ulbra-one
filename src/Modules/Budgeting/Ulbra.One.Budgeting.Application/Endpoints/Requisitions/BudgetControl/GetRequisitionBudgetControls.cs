using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Budgeting.Application.BudgetControl;
using Ulbra.One.Budgeting.Application.Contracts;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions.BudgetControl;

/// <summary>Query-string binding for the batch: the requisition ids as repeated `ids` keys.</summary>
public sealed record RequisitionBudgetControlsRequest
{
    [SwaggerSchema("Requisitions to evaluate, as repeated `ids` query keys. The verdict is returned keyed by "
        + "requisition id; ids that do not exist are omitted.")]
    public string[]? Ids { get; set; }
}

/// <summary>
/// Batch of <see cref="GetRequisitionBudgetControl"/>: evaluates several requisitions in one call so an
/// approval/triage queue of N items does not fan out into N per-id requests. Same verdict and ledger per item.
/// </summary>
public sealed class GetRequisitionBudgetControls : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/budget-controls", Handle)
            .WithName("GetRequisitionBudgetControls").WithTags("Budgeting")
            .WithSummary("Describes the budget impact of several requisitions at once.")
            .WithDescription("Batch of GetRequisitionBudgetControl: the candidate verdict (breaches, "
                + "unbudgeted, exceeds) plus the per-month ledger for each requisition, keyed by id, in one "
                + "call — so an approval/triage queue of N requisitions does not fan out into N requests.")
            .RequirePermission(Permissions.RequisitionsRead)
            .Produces<RequisitionBudgetControlsResponse>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        [AsParameters] RequisitionBudgetControlsRequest req,
        IBudgetControl budgetControl,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var ids = (req.Ids ?? [])
            .Select(s => RequisitionId.TryParse(s, null, out var id) ? id.Value : (Guid?)null)
            .Where(g => g.HasValue).Select(g => g!.Value).Distinct().ToArray();
        if (ids.Length == 0)
            return TypedResults.Ok(new RequisitionBudgetControlsResponse(new Dictionary<string, BudgetControlResponse>()));

        var requisitions = await uow.ReadSet<Requisition>().Where(r => ids.Contains(r.Id)).ToListAsync(ct);
        var controls = new Dictionary<string, BudgetControlResponse>(requisitions.Count);
        foreach (var r in requisitions)
        {
            var evaluation = await budgetControl.EvaluateCandidateAsync(r, ct);
            controls[new RequisitionId(r.Id).ToString()] = BudgetControlResponse.From(evaluation, r.EstimatedContributionFor);
        }
        return TypedResults.Ok(new RequisitionBudgetControlsResponse(controls));
    }
}
