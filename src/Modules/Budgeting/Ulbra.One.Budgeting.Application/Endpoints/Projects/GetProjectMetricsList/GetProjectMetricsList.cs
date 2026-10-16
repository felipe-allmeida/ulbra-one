using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Budgeting.Application.Contracts;
using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Budgeting.Application.Endpoints.Projects.GetProjectMetricsList;

/// <summary>Query-string binding for the batch: the project ids as repeated `ids` keys.</summary>
public sealed record ProjectMetricsListRequest
{
    [SwaggerSchema("Projects to load metrics for, as repeated `ids` query keys. Metrics are returned keyed "
        + "by project id; ids that do not exist are omitted.")]
    public string[]? Ids { get; set; }
}

/// <summary>
/// Batch of <see cref="GetProjectMetrics.GetProjectMetrics"/>: metrics for several projects in one call, so a
/// dashboard listing N projects does not fan out into N per-id requests. All linked requisitions load in one
/// query and are grouped in memory. Same figures and exclusions as the per-project endpoint.
/// </summary>
public sealed class GetProjectMetricsList : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/metrics", Handle)
            .WithName("GetProjectMetricsList").WithTags("Budgeting")
            .WithSummary("Gets budget tracking for several projects at once.")
            .WithDescription("Batch of GetProjectMetrics: accumulated budgeted, forecast, realized and "
                + "balance per project, keyed by id, in one call. Forecast covers the whole requisition "
                + "pipeline; only requisitions that reached payment consume the real balance; realized counts "
                + "settled, non-reversed payments; cancelled requisitions are excluded.")
            .RequirePermission(Permissions.ProjectsRead)
            .Produces<ProjectMetricsListResponse>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        [AsParameters] ProjectMetricsListRequest req, IUnitOfWork uow, CancellationToken ct)
    {
        var ids = (req.Ids ?? [])
            .Select(s => ProjectId.TryParse(s, null, out var id) ? id.Value : (Guid?)null)
            .Where(g => g.HasValue).Select(g => g!.Value).Distinct().ToArray();
        if (ids.Length == 0)
            return TypedResults.Ok(new ProjectMetricsListResponse(new Dictionary<string, ProjectMetricsResponse>()));

        var projects = await uow.ReadSet<Project>().Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        var projectIds = projects.Select(p => p.Id).ToList();

        // All linked requisitions in one query, grouped in memory — avoids the per-project N+1.
        var linked = await uow.ReadSet<Requisition>()
            .Where(r => r.ProjectId != null && projectIds.Contains(r.ProjectId.Value)
                && r.Status != RequisitionStatus.Cancelled)
            .Include(r => r.Payments)
            .ToListAsync(ct);
        var byProject = linked.GroupBy(r => r.ProjectId!.Value).ToDictionary(g => g.Key, g => g.ToList());

        var metrics = new Dictionary<string, ProjectMetricsResponse>(projects.Count);
        foreach (var p in projects)
        {
            var rs = byProject.GetValueOrDefault(p.Id) ?? [];
            // Forecast = whole pipeline; only what reached payment (InPayment+) reduces the real balance.
            var costs = rs.Select(r => r.TotalCost()).ToList();
            var committed = rs.Where(r => ReachedPayment(r.Status)).Select(r => r.TotalCost()).ToList();
            var paid = rs.Select(r => r.RealizedValue).ToList();
            var m = p.CalculateMetrics(costs, committed, paid);
            metrics[new ProjectId(p.Id).ToString()] = ProjectMetricsResponse.From(p, m, rs.Count);
        }
        return TypedResults.Ok(new ProjectMetricsListResponse(metrics));
    }

    private static bool ReachedPayment(RequisitionStatus s) =>
        s is RequisitionStatus.InPayment or RequisitionStatus.AwaitingFiscalDocument
            or RequisitionStatus.Paid or RequisitionStatus.Finalized;
}
