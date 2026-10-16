using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Budgeting.Application.BudgetControl;
using Ulbra.One.Budgeting.Application.Contracts;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions.BudgetControl;

/// <summary>Total realized (spend) per cost center for a company, summed over all competencies — feeds the
/// cost-center list card (spend × annual budget). Read-only; requires requisitions:read.</summary>
public sealed class GetCostCenterRealized : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/cost-center-realized", Handle)
            .WithName("GetCostCenterRealized").WithTags("Budgeting")
            .WithSummary("Totals realized spend per cost center.")
            .WithDescription("Total realized spend per cost center for one company, summed over every "
                + "competency — feeds the cost-center list card (spend against the annual budget). "
                + "Read-only.")
            .RequirePermission(Permissions.RequisitionsRead)
            .Produces<IReadOnlyList<CostCenterRealizedResponse>>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        [SwaggerSchema("Company whose cost centers are totalled. Required.")] CompanyId companyId,
        IBudgetControl budgetControl,
        CancellationToken ct)
    {
        var rows = await budgetControl.SummarizeRealizedByCostCenterAsync(companyId.Value, ct);
        return TypedResults.Ok(rows.Select(CostCenterRealizedResponse.From).ToList());
    }
}
