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

/// <summary>Query-string parameters for the cost-center budget summary. Bound via <c>[AsParameters]</c>.</summary>
public sealed record GetCostCenterBudgetSummaryRequest
{
    [SwaggerSchema("Company to summarize. Required.")]
    public CompanyId CompanyId { get; set; }

    [SwaggerSchema("Restrict to a single unit. Omit to consolidate across every unit of the company.")]
    public UnitId? UnitId { get; set; }

    [SwaggerSchema("Accounting competency (`YYYY-MM`) to summarize. Required; an unparseable value returns 400.")]
    [SwaggerExample("2026-08")]
    public string Competency { get; set; } = string.Empty;
}

/// <summary>Server-computed budget summary by cost center (natureza) for a company in a single competency —
/// powers the role-home "Orçamento por natureza" panel. <c>unitId</c> is optional (omit = consolidated across
/// the company's units; set = only that unit). Read-only; requires requisitions:read.</summary>
public sealed class GetCostCenterBudgetSummary : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/budget-summary", Handle)
            .WithName("GetCostCenterBudgetSummary").WithTags("Budgeting")
            .WithSummary("Summarizes the budget by cost center.")
            .WithDescription("Server-computed budget summary by cost center (natureza) for one company in a "
                + "single competency — the figures behind the role-home \"Orçamento por natureza\" panel. "
                + "Read-only.")
            .RequirePermission(Permissions.RequisitionsRead)
            .Produces<IReadOnlyList<CostCenterBudgetLineResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(
        [AsParameters] GetCostCenterBudgetSummaryRequest req,
        UserContext user,
        IUserScopeDirectory userScope,
        IBudgetControl budgetControl,
        CancellationToken ct)
    {
        var comp = CompetencyFormat.Normalize(req.Competency);
        if (!CompetencyFormat.IsValid(comp))
            return ErrorResults.InvalidRequest("competency is not a valid \"YYYY-MM\" value.", ErrorCodes.Requisition.CompetencyInvalid);

        // Scoped roles (Requester/Approver) see only their granted units × naturezas; everyone else bypasses (AEL-883).
        var scope = await RequisitionScope.ResolveAsync(user, userScope, ct);
        var unitIds = RequisitionScope.EffectiveUnitIds(scope, req.UnitId?.Value);
        var lines = await budgetControl.SummarizeByCostCenterAsync(req.CompanyId.Value, unitIds, scope.CostCenterIds, comp, ct);
        return TypedResults.Ok(lines.Select(CostCenterBudgetLineResponse.From).ToList());
    }
}
