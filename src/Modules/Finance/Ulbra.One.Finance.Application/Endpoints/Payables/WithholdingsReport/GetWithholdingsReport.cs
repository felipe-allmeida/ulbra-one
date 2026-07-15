using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.WithholdingsReport;

/// <summary>Consolidated withholding report: every payable that carries withholdings, grouped totals per atomic
/// tax, and headline gross/withheld/net. Optional <c>companyId</c> filters to one company (omitted = all).</summary>
public sealed class GetWithholdingsReport : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("", Handle).WithName("GetWithholdingsReport").WithTags("Finance")
            .RequirePermission(Permissions.PayablesRead).Produces<WithholdingsReportResponse>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        CompanyId? companyId,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        // Only payables that actually carry withholdings; skip terminal-negative states. Withholdings load via
        // AutoInclude (PayableConfiguration), so no explicit Include is needed.
        var q = uow.ReadSet<Payable>()
            .Where(p => p.Status != PayableStatus.Cancelled && p.Status != PayableStatus.Rejected
                        && p.Withholdings.Any());
        if (companyId is { } cid)
            q = q.Where(p => p.CompanyId == cid.Value);

        var payables = await q.OrderByDescending(p => p.CreatedAt).ToListAsync(ct);

        var byPayable = payables
            .Select(p => new WithholdingPayableRow(
                new PayableId(p.Id), new VendorId(p.VendorId), p.CounterpartyName, p.DocumentNumber,
                p.Status, p.TotalAmount, p.TotalWithheld, p.NetTotal,
                p.Withholdings.OrderBy(w => w.TaxType).Select(PayableWithholdingResponse.From).ToList()))
            .ToList();

        var byTax = payables
            .SelectMany(p => p.Withholdings)
            .GroupBy(w => w.TaxType)
            .Select(g => new WithholdingByTaxRow(g.Key, g.Sum(w => w.BaseAmount), g.Sum(w => w.Amount),
                g.Select(w => w.PayableId).Distinct().Count()))
            .OrderBy(r => r.TaxType)
            .ToList();

        var totals = new WithholdingsTotals(
            payables.Sum(p => p.TotalAmount),
            payables.Sum(p => p.TotalWithheld),
            payables.Sum(p => p.NetTotal),
            payables.Count);

        return TypedResults.Ok(new WithholdingsReportResponse(totals, byTax, byPayable));
    }
}
