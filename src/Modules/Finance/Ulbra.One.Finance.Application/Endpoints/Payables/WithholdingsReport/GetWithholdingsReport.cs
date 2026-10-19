using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
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
            .WithSummary("Reports withholdings across payables.")
            .WithDescription("Every payable carrying source withholdings, with totals per tax and headline gross, withheld "
                + "and net figures. This is the Finance-side view built from what is recorded on the payables; "
                + "the Fiscal module\u0027s assessment is the one that drives EFD-Reinf.")
            .RequirePermission(Permissions.PayablesRead).Produces<WithholdingsReportResponse>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        [SwaggerSchema("Company to report on. Omit to include every company.")]
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

        // Build the per-payable rows, splitting each retention into realized (retido) vs foreseen (previsto).
        // Emission taxes (IRRF/INSS/ISS) are realized at competence; the CSRF group is realized only at each
        // baixa, so its realized part is the sum of the non-reversed PaymentWithholding rows for that tax.
        var byPayable = payables
            .Select(p =>
            {
                var paidByTax = p.Installments
                    .SelectMany(i => i.Payments)
                    .Where(pay => !pay.IsReversed)
                    .SelectMany(pay => pay.Withholdings)
                    .GroupBy(pw => pw.TaxType)
                    .ToDictionary(g => g.Key, g => g.Sum(pw => pw.Amount));

                var lines = p.Withholdings
                    .OrderBy(w => w.TaxType)
                    .Select(w =>
                    {
                        var timing = WithholdingTimings.For(w.TaxType);
                        var realized = timing == WithholdingTiming.OnIssue
                            ? w.Amount
                            : Math.Min(w.Amount, paidByTax.GetValueOrDefault(w.TaxType));
                        return new WithholdingReportLine(w.TaxType, w.BaseAmount, w.Rate, w.Amount, timing,
                            realized, w.Amount - realized);
                    })
                    .ToList();

                return new WithholdingPayableRow(
                    new PayableId(p.Id), new VendorId(p.VendorId), p.CounterpartyName, p.DocumentNumber,
                    p.Status, p.TotalAmount, p.TotalWithheld, p.NetTotal, lines,
                    lines.Sum(l => l.RealizedAmount), lines.Sum(l => l.ForeseenAmount));
            })
            .ToList();

        // Aggregate the same lines per tax.
        var byTax = byPayable
            .SelectMany(p => p.Lines.Select(l => (p.Id, l)))
            .GroupBy(x => x.l.TaxType)
            .Select(g => new WithholdingByTaxRow(
                g.Key, g.Sum(x => x.l.BaseAmount), g.Sum(x => x.l.Amount), g.Select(x => x.Id).Distinct().Count(),
                g.Sum(x => x.l.RealizedAmount), g.Sum(x => x.l.ForeseenAmount)))
            .OrderBy(r => r.TaxType)
            .ToList();

        var totals = new WithholdingsTotals(
            payables.Sum(p => p.TotalAmount),
            payables.Sum(p => p.TotalWithheld),
            payables.Sum(p => p.NetTotal),
            payables.Count,
            byPayable.Sum(p => p.RealizedAmount),
            byPayable.Sum(p => p.ForeseenAmount));

        return TypedResults.Ok(new WithholdingsReportResponse(totals, byTax, byPayable));
    }
}
