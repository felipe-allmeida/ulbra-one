using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Budgeting.Application.Contracts;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions.StageCounts;

/// <summary>Query-string filters for the stage-count summary, bound via <c>[AsParameters]</c>.</summary>
public sealed record RequisitionStageCountsRequest
{
    [SwaggerSchema("Company that owns the requisitions. Omit for the consolidated view across every company, "
        + "which only a consolidated or scoped role may request.")]
    public CompanyId? CompanyId { get; set; }

    [SwaggerSchema("Count only requisitions created by the authenticated caller (requester dashboard). "
        + "The owner is resolved server-side from the token, never from the query string.")]
    public bool? Mine { get; set; }
}

/// <summary>
/// Counts requisitions grouped by workflow status (with the summed estimated value per status) in a single
/// grouped query. Replaces the former per-status fan-out on the client — the requester aside/list/dashboard
/// each fired one PageSize=1 list request per status just to read a total.
/// </summary>
public sealed class GetRequisitionStageCounts : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("/stage-counts", Handle)
            .WithName("GetRequisitionStageCounts")
            .WithTags("Budgeting")
            .WithSummary("Counts requisitions per workflow status.")
            .WithDescription("Returns the count and summed estimated value of requisitions grouped by "
                + "workflow status, honouring the same company / \"mine\" / role scope as the browse list. "
                + "Omitting CompanyId asks for the consolidated view across all companies (403 unless the "
                + "caller holds a consolidated role); a scoped role always reads within its scope, fail-closed.")
            .RequirePermission(Permissions.RequisitionsRead)
            .Produces<RequisitionStageCountsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> Handle(
        [AsParameters] RequisitionStageCountsRequest req,
        UserContext userContext,
        IUserScopeDirectory userScope,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        if (RequisitionScope.GuardConsolidated(req.CompanyId, userContext) is { } forbidden)
            return forbidden;

        var query = await RequisitionScope.ApplyAsync(
            uow.ReadSet<Requisition>(), req.CompanyId, req.Mine == true, userContext, userScope, ct);

        // One grouped round-trip: a COUNT + SUM per status. Enum is projected as-is and stringified in memory
        // (its name is what the client keys on); Status persists as string so the group key is stable.
        var rows = await query
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Total = g.Sum(x => x.EstimateValue) })
            .ToListAsync(ct);

        var stages = rows.ToDictionary(
            r => r.Status.ToString(),
            r => new RequisitionStageCountResponse(r.Count, r.Total));

        return TypedResults.Ok(new RequisitionStageCountsResponse(stages));
    }
}
