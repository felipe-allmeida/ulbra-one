using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Budgeting.Application.Contracts;
using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Budgeting.Application.Endpoints.Projects.GetProjectMetrics;

/// <summary>
/// Accumulated tracking for a project: orçado × previsto × realizado × saldo, plus the overrun flag the
/// approval panel (AEL-790) surfaces.
/// </summary>
public sealed class GetProjectMetrics : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("/{id}/metrics", Handle)
            .WithName("GetProjectMetrics")
            .WithTags("Budgeting")
            .WithSummary("Gets a project's budget tracking.")
            .WithDescription("Accumulated figures for one project: budgeted, forecast, realized and "
                + "balance, plus the overrun flag the approval panel surfaces. Forecast covers the whole "
                + "requisition pipeline; only requisitions that reached payment consume the real balance, "
                + "and realized counts settled, non-reversed payments. Cancelled requisitions are excluded.")
            .RequirePermission(Permissions.ProjectsRead)
            .Produces<ProjectMetricsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(ProjectId id, IUnitOfWork uow, CancellationToken ct)
    {
        var project = await uow.ReadSet<Project>().FirstOrDefaultAsync(p => p.Id == id.Value, ct);
        if (project is null)
            return ErrorResults.NotFound("Project not found.", ErrorCodes.Project.NotFound);

        var linked = await uow.ReadSet<Requisition>()
            .Where(r => r.ProjectId == id.Value && r.Status != RequisitionStatus.Cancelled)
            .Include(r => r.Payments)
            .ToListAsync(ct);

        // Forecast = cost of the whole pipeline (projection); only what reached payment (InPayment+) reduces the
        // real BALANCE, consistent with the per-competency budget (AEL-844).
        var costs = linked.Select(r => r.TotalCost()).ToList();
        var committed = linked.Where(r => ReachedPayment(r.Status)).Select(r => r.TotalCost()).ToList();

        // Real "realized": Σ non-reversed payments actually settled in Finance.
        var paid = linked.Select(r => r.RealizedValue).ToList();

        var metrics = project.CalculateMetrics(costs, committed, paid);
        return TypedResults.Ok(ProjectMetricsResponse.From(project, metrics, linked.Count));
    }

    /// <summary>Requisition that already reached payment (InPayment onward) — only these consume the real balance.</summary>
    private static bool ReachedPayment(RequisitionStatus s) =>
        s is RequisitionStatus.InPayment or RequisitionStatus.AwaitingFiscalDocument
            or RequisitionStatus.Paid or RequisitionStatus.Finalized;
}
