using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Budgeting.Application.BudgetControl;
using Ulbra.One.Budgeting.Application.Contracts;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using CompetencyFormat = Ulbra.One.Common.Domain.Competency;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions.BudgetControl;

/// <summary>Query-string parameters for the pre-requisition budget panel. Bound via <c>[AsParameters]</c>.</summary>
public sealed record GetDimensionBudgetControlRequest
{
    [SwaggerSchema("Cost center whose budget window is described. Required.")]
    public CostCenterId CostCenterId { get; set; }

    [SwaggerSchema("Unit the spend would be charged to. Required.")]
    public UnitId UnitId { get; set; }

    [SwaggerSchema("First competency of the window (`YYYY-MM`). Required; an unparseable value returns 400.")]
    [SwaggerExample("2026-01")]
    public string CompetencyFrom { get; set; } = string.Empty;

    [SwaggerSchema("Last competency of the window (`YYYY-MM`), inclusive. Omit for a 12-month horizon "
        + "starting at CompetencyFrom. A value before CompetencyFrom returns 400.")]
    [SwaggerExample("2026-12")]
    public string? CompetencyTo { get; set; }

    [SwaggerSchema("Requisition to leave out of the consumed figures — pass the one being edited so the "
        + "form can overlay its own unsaved value without double-counting.")]
    public RequisitionId? ExcludeRequisitionId { get; set; }
}

/// <summary>Server-computed budget panel for a (cost center, unit) window, before a requisition exists — used
/// by the create form's live "Previsão de Saldo". Returns the per-month ledger (orçado × consumido × previsto)
/// excluding one requisition; the form overlays its own unsaved value on top. No candidate verdict here (the
/// authoritative block runs at submit/approve).</summary>
public sealed class GetDimensionBudgetControl : IEndpoint
{
    /// <summary>Default panel horizon when no end competency is given: 12 months from the start.</summary>
    private const int DefaultHorizonMonths = 12;

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/budget-control", Handle)
            .WithName("GetDimensionBudgetControl").WithTags("Budgeting")
            .WithSummary("Describes the budget for a cost center / unit window.")
            .WithDescription("Per-month budget ledger (budgeted, consumed, forecast) for a (cost center, "
                + "unit) pair before any requisition exists — the live \"Previsão de Saldo\" on the create "
                + "form. Returns no candidate verdict: the authoritative block runs at submit and approve.")
            .RequirePermission(Permissions.RequisitionsRead)
            .Produces<BudgetControlResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(
        [AsParameters] GetDimensionBudgetControlRequest req,
        IBudgetControl budgetControl,
        CancellationToken ct)
    {
        var from = CompetencyFormat.Normalize(req.CompetencyFrom);
        if (!CompetencyFormat.IsValid(from))
            return ErrorResults.InvalidRequest("competencyFrom is not a valid \"YYYY-MM\" value.", ErrorCodes.Requisition.CompetencyInvalid);

        var to = string.IsNullOrWhiteSpace(req.CompetencyTo) ? CompetencyFormat.AddMonths(from, DefaultHorizonMonths - 1) : CompetencyFormat.Normalize(req.CompetencyTo);
        if (!CompetencyFormat.IsValid(to) || CompetencyFormat.Compare(to, from) < 0)
            return ErrorResults.InvalidRequest("competencyTo is invalid or precedes competencyFrom.", ErrorCodes.Requisition.CompetencyRangeInvalid);

        var months = await budgetControl.DescribeAsync(req.CostCenterId.Value, req.UnitId.Value, from, to, req.ExcludeRequisitionId?.Value, ct);
        return TypedResults.Ok(BudgetControlResponse.FromMonths(months));
    }
}
