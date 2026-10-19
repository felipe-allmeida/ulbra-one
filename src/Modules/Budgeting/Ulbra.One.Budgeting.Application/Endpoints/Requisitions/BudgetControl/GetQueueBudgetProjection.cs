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

/// <summary>Query-string parameters for the queue budget projection. Bound via <c>[AsParameters]</c>.</summary>
public sealed record GetQueueBudgetProjectionRequest
{
    [SwaggerSchema("Company whose queue is projected. Required.")]
    public CompanyId CompanyId { get; set; }

    [SwaggerSchema("Restrict to a single unit. Omit to project the whole company.")]
    public UnitId? UnitId { get; set; }

    [SwaggerSchema("First competency of the horizon (`YYYY-MM`). Omitted or unparseable falls back to the "
        + "current month.")]
    [SwaggerExample("2026-01")]
    public string? From { get; set; }

    [SwaggerSchema("Last competency of the horizon (`YYYY-MM`), inclusive. Omitted or unparseable falls "
        + "back to 11 months after From, for a 12-month window. A value before From returns 400.")]
    [SwaggerExample("2026-12")]
    public string? To { get; set; }
}

/// <summary>Server-computed multi-month projection of a company/unit's whole queue against the budget —
/// powers the role-home "Impacto no orçamento / Se aprovar a fila" sparkline. <c>unitId</c> is optional
/// (omit = whole company). <c>from</c>/<c>to</c> ("YYYY-MM") default to the current month + 11 (12-month
/// horizon) when omitted. Read-only; requires requisitions:read.</summary>
public sealed class GetQueueBudgetProjection : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/budget-projection", Handle)
            .WithName("GetQueueBudgetProjection").WithTags("Budgeting")
            .WithSummary("Projects the pending queue against the budget.")
            .WithDescription("Multi-month projection of a company's (or unit's) whole pending queue against "
                + "the budget — the \"Impacto no orçamento / Se aprovar a fila\" sparkline on the role home. "
                + "Read-only.")
            .RequirePermission(Permissions.RequisitionsRead)
            .Produces<QueueProjectionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(
        [AsParameters] GetQueueBudgetProjectionRequest req,
        UserContext user,
        IUserScopeDirectory userScope,
        IBudgetControl budgetControl,
        CancellationToken ct)
    {
        var start = CompetencyFormat.Normalize(req.From);
        if (!CompetencyFormat.IsValid(start))
            start = CompetencyFormat.FromDate(DateTimeOffset.UtcNow);

        var end = CompetencyFormat.Normalize(req.To);
        if (!CompetencyFormat.IsValid(end))
            end = CompetencyFormat.AddMonths(start, 11);

        if (CompetencyFormat.Compare(end, start) < 0)
            return ErrorResults.InvalidRequest("to must not be before from.", ErrorCodes.Requisition.CompetencyRangeInvalid);

        // Scoped roles (Requester/Approver) project only their granted units × naturezas; everyone else bypasses (AEL-883).
        var scope = await RequisitionScope.ResolveAsync(user, userScope, ct);
        var unitIds = RequisitionScope.EffectiveUnitIds(scope, req.UnitId?.Value);
        var projection = await budgetControl.SummarizeQueueProjectionAsync(req.CompanyId.Value, unitIds, scope.CostCenterIds, start, end, ct);
        return TypedResults.Ok(QueueProjectionResponse.From(projection));
    }
}
