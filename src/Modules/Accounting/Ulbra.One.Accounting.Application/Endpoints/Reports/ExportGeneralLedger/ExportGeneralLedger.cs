using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Accounting.Application.CostCenters;
using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Accounting.Application.Endpoints.Reports.ExportGeneralLedger;

public sealed record ExportGeneralLedgerRequest
{
    [SwaggerSchema("Company to report on. Omit to consolidate across every company.")]
    public CompanyId? CompanyId { get; set; }
    [SwaggerSchema("Restrict to a single ledger account. Takes precedence over Root when both are sent.")]
    public LedgerAccountId? LedgerAccountId { get; set; }
    [SwaggerSchema("Restrict to an account root — the first digit of the code: 1 asset, 2 liability, 3 revenue, "
        + "4 expense, 5 cost. Ignored when LedgerAccountId is set.")]
    [SwaggerExample("4")]
    public string? Root { get; set; }
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

/// <summary>Exports the Livro Razão as an Excel-friendly CSV (UTF-8 BOM, <c>;</c>-separated, comma decimals).
/// Same filters as the GET, but NOT paginated — every qualifying account. Read-only.</summary>
public sealed class ExportGeneralLedger : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/ledger/export", Handle).WithName("ExportGeneralLedger").WithTags("Accounting")
            .WithSummary("Exports the general ledger as a file.")
            .WithDescription("The same lines as the general ledger, as a downloadable file, and NOT paginated - the export "
                + "always covers the full filtered range.")
            .RequirePermission(Permissions.AccountingRead)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(
        IUnitOfWork uow, ICostCenterLookup costCenters, [AsParameters] ExportGeneralLedgerRequest req,
        CancellationToken ct)
    {
        if (req.From is { } f && req.To is { } t && t < f)
            return ErrorResults.InvalidRequest("'to' must not precede 'from'.", ErrorCodes.GeneralLedger.InvalidRange);

        var accountIds = await GeneralLedgerAccountQuery
            .Qualifying(uow, req.CompanyId?.Value, req.LedgerAccountId?.Value, req.Root, req.CostCenterId?.Value, req.To, req.UnitId?.Value)
            .Select(a => a.Id)
            .ToListAsync(ct);

        var rows = await GeneralLedgerLoader.LoadAsync(
            uow, req.CompanyId?.Value, accountIds, req.CostCenterId?.Value, req.To, ct, req.UnitId?.Value);
        var sections = GeneralLedgerBuilder.Build(rows, req.From, req.To);

        var ccIds = sections
            .SelectMany(s => s.Movements)
            .Where(m => m.CostCenterId is not null)
            .Select(m => m.CostCenterId!.Value)
            .Distinct()
            .ToList();
        var ccInfo = await costCenters.GetAsync(ccIds, ct);
        var ccLabels = ccInfo.ToDictionary(kv => kv.Key, kv => Label(kv.Value));

        var csv = GeneralLedgerCsv.Build(sections, ccLabels);
        // Prepend the UTF-8 BOM so Excel (pt-BR) detects the encoding and renders accents correctly.
        // (Encoding.GetBytes does NOT emit the preamble, so add it explicitly.)
        byte[] bom = [0xEF, 0xBB, 0xBF];
        var bytes = bom.Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
        return TypedResults.File(bytes, "text/csv", "livro-razao.csv");
    }

    private static string Label(CostCenterInfo cc) =>
        string.IsNullOrWhiteSpace(cc.Code) ? cc.Name : $"{cc.Code} — {cc.Name}";
}
