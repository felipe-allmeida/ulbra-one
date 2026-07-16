using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Accounting.Application.CostCenters;
using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Accounting.Application.Endpoints.Reports.ExportGeneralLedger;

public sealed record ExportGeneralLedgerRequest
{
    public CompanyId? CompanyId { get; set; }
    public LedgerAccountId? LedgerAccountId { get; set; }
    public string? Root { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public CostCenterId? CostCenterId { get; set; }
}

/// <summary>Exports the Livro Razão as an Excel-friendly CSV (UTF-8 BOM, <c>;</c>-separated, comma decimals).
/// Same filters as the GET, but NOT paginated — every qualifying account. Read-only.</summary>
public sealed class ExportGeneralLedger : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/ledger/export", Handle).WithName("ExportGeneralLedger").WithTags("Accounting")
            .RequirePermission(Permissions.AccountingRead)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(
        IUnitOfWork uow, ICostCenterLookup costCenters, [AsParameters] ExportGeneralLedgerRequest req,
        CancellationToken ct)
    {
        if (req.From is { } f && req.To is { } t && t < f)
            return TypedResults.Problem(detail: "'to' must not precede 'from'.",
                statusCode: StatusCodes.Status400BadRequest, title: "GeneralLedger.InvalidRange");

        var accountIds = await GeneralLedgerAccountQuery
            .Qualifying(uow, req.CompanyId?.Value, req.LedgerAccountId?.Value, req.Root, req.CostCenterId?.Value, req.To)
            .Select(a => a.Id)
            .ToListAsync(ct);

        var rows = await GeneralLedgerLoader.LoadAsync(
            uow, req.CompanyId?.Value, accountIds, req.CostCenterId?.Value, req.To, ct);
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
