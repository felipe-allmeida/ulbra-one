using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Budgeting.Application.Contracts;
using Ulbra.One.Budgeting.Application.Reports;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain;

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions.SlaReport;

/// <summary>Query-string parameters for the requisition SLA report. Bound via <c>[AsParameters]</c>.</summary>
public sealed record GetRequisitionSlaReportRequest
{
    [SwaggerSchema("Restrict to one company. Omit to report across every company.")]
    public CompanyId? CompanyId { get; set; }

    [SwaggerSchema("Restrict to one unit. Omit to report across every unit.")]
    public UnitId? UnitId { get; set; }

    [SwaggerSchema("First day of the history window, inclusive, read in São Paulo time. Omit for the full "
        + "history. Scopes the analytics only — the live overdue queue is always current.")]
    [SwaggerExample("2026-01-01")]
    public DateOnly? From { get; set; }

    [SwaggerSchema("Last day of the history window, inclusive, read in São Paulo time. Omit for the full "
        + "history. A value before From returns 400.")]
    [SwaggerExample("2026-12-31")]
    public DateOnly? To { get; set; }
}

/// <summary>SLA analysis (AEL-872): live overdue queue (from the head scalar) + historical analytics from the
/// transition trail (avg per stage, % within SLA, cycle, per-unit ranking). <c>from</c>/<c>to</c> scope the
/// history (BRT); omitted = full history. ADM + Approver only.</summary>
public sealed class GetRequisitionSlaReport : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/sla-report", Handle).WithName("GetRequisitionSlaReport").WithTags("Budgeting")
            .WithSummary("Reports requisition SLA performance.")
            .WithDescription("Two things at once: the live queue of requisitions already past their stage "
                + "SLA, and historical analytics computed from the transition trail — average time per "
                + "stage, share resolved within SLA, full cycle time and a per-unit ranking. The date "
                + "window scopes the history only and is read in São Paulo time.")
            .RequirePermission(Permissions.RequisitionsSlaReport)
            .Produces<RequisitionSlaReportResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(
        [AsParameters] GetRequisitionSlaReportRequest req,
        IOptions<RequisitionSlaOptions> options, TimeProvider clock, IUnitOfWork uow, CancellationToken ct)
    {
        var (companyId, unitId, from, to) = (req.CompanyId, req.UnitId, req.From, req.To);
        if (from is { } f && to is { } t && t < f)
            return ErrorResults.InvalidRequest("'to' must not precede 'from'.", ErrorCodes.SlaReport.InvalidRange);

        var sla = options.Value;
        // Convert the BRT day boundaries to UTC — Npgsql maps DateTimeOffset→timestamptz and only accepts
        // offset 0. Same instant; the calculator compares by absolute instant so this is transparent to it.
        var fromInstant = from is { } ff ? BusinessTime.SaoPauloDayStart(ff).ToUniversalTime() : (DateTimeOffset?)null;
        // Guard the AddDays overflow at DateOnly.MaxValue (a `to=9999-12-31` would throw → 500); such a far
        // upper bound is effectively "no bound", so leave it open.
        var toExclusive = to is { } tt && tt < DateOnly.MaxValue
            ? BusinessTime.SaoPauloDayStart(tt.AddDays(1)).ToUniversalTime()
            : (DateTimeOffset?)null;
        var now = clock.GetUtcNow();

        var open = await RequisitionSlaLoader.LoadOpenStagesAsync(uow, companyId?.Value, unitId?.Value, ct);
        var history = await RequisitionSlaLoader.LoadHistoryAsync(
            uow, companyId?.Value, unitId?.Value, fromInstant, toExclusive, ct);
        var report = RequisitionSlaCalculator.Build(
            open, history, fromInstant, toExclusive, sla.TriageHours, sla.ApprovalHours, now);

        return TypedResults.Ok(RequisitionSlaReportResponse.Of(
            report, companyId, unitId, from, to, now, sla.TriageHours, sla.ApprovalHours));
    }
}
