using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Accounting.Application.Endpoints.Reports.ExportTrialBalance;

public sealed record ExportTrialBalanceRequest
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

/// <summary>Exports the Balancete as an Excel-friendly CSV (UTF-8 BOM, <c>;</c>-separated, comma decimals). Same
/// filters as <c>GET /accounting/trial-balance</c>, every row (not paginated). Read-only.</summary>
public sealed class ExportTrialBalance : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/trial-balance/export", Handle).WithName("ExportTrialBalance").WithTags("Accounting")
            .WithSummary("Exports the trial balance as a file.")
            .WithDescription("The same figures as the trial balance report, as a downloadable file. Takes the same "
                + "filters.")
            .RequirePermission(Permissions.AccountingRead)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(
        IUnitOfWork uow, [AsParameters] ExportTrialBalanceRequest req, CancellationToken ct)
    {
        if (req.From is { } f && req.To is { } t && t < f)
            return ErrorResults.InvalidRequest("'to' must not precede 'from'.", ErrorCodes.TrialBalance.InvalidRange);

        var lines = await PostedLinesLoader.LoadAsync(uow, req.CompanyId?.Value, ct, req.CostCenterId?.Value, req.UnitId?.Value);
        var trialBalance = TrialBalanceCalculator.Build(lines, req.From, req.To);

        var csv = TrialBalanceCsv.Build(trialBalance);
        // Prepend the UTF-8 BOM so Excel (pt-BR) detects the encoding and renders accents correctly.
        byte[] bom = [0xEF, 0xBB, 0xBF];
        var bytes = bom.Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
        return TypedResults.File(bytes, "text/csv", "balancete.csv");
    }
}
