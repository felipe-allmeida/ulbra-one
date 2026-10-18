using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Accounting.Application.Contracts;
using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Accounting.Application.Endpoints.Reports.GetIncomeStatement;

public sealed record GetIncomeStatementRequest
{
    [SwaggerSchema("Company to report on. Omit to consolidate across every company.")]
    public CompanyId? CompanyId { get; set; }
    [SwaggerSchema("Start of the competence range, inclusive. Filters on the entry\u0027s competence date, not "
        + "on when it was posted.")]
    [SwaggerExample("2026-01-01")]
    public DateOnly? From { get; set; }
    [SwaggerSchema("End of the competence range, inclusive.")]
    [SwaggerExample("2026-12-31")]
    public DateOnly? To { get; set; }
    [SwaggerSchema("Restrict to entry lines charged to this cost center. Lines without one are excluded.")]
    public CostCenterId? CostCenterId { get; set; }
    [SwaggerSchema("Restrict to entry lines charged to this unit. Lines without one are excluded.")]
    public UnitId? UnitId { get; set; }
}

/// <summary>Structured DRE (cascade) for a competence range, consolidated (no company) or per company, optionally
/// filtered by cost center. <c>from</c>/<c>to</c> default to the full history up to today.</summary>
public sealed class GetIncomeStatement : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/income-statement", Handle).WithName("GetIncomeStatement").WithTags("Accounting")
            .WithSummary("Gets the income statement.")
            .WithDescription("DRE for the range, built by rolling result accounts up through the cascade lines configured "
                + "on the chart. Only posted entries count. An account without a cascade line does not appear, "
                + "which is the usual reason a figure looks short.")
            .RequirePermission(Permissions.AccountingRead).Produces<IncomeStatementResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(
        IUnitOfWork uow, TimeProvider clock, [AsParameters] GetIncomeStatementRequest req, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var rangeFrom = req.From ?? DateOnly.MinValue;
        var rangeTo = req.To ?? today;
        if (rangeTo < rangeFrom)
            return ErrorResults.InvalidRequest("'to' must not precede 'from'.", ErrorCodes.IncomeStatement.InvalidRange);

        var lines = await PostedLinesLoader.LoadAsync(uow, req.CompanyId?.Value, ct, req.CostCenterId?.Value, req.UnitId?.Value);
        var dre = IncomeStatementCalculator.Build(lines, rangeFrom, rangeTo);
        return TypedResults.Ok(IncomeStatementResponse.Of(dre, req.CompanyId, req.CostCenterId));
    }
}
