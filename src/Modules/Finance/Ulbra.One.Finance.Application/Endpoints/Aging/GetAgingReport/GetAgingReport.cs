using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Aging.GetAgingReport;

public sealed class GetAgingReport : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("", Handle).WithName("GetAgingReport").WithTags("Finance")
            .RequireAuthorization().Produces<AgingReportResponse>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        CompanyId companyId,
        DateOnly? asOf,
        [FromKeyedServices("finance")] IUnitOfWork uow,
        TimeProvider clock,
        CancellationToken ct)
    {
        var today = asOf ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        // Bucket boundaries as date constants (verified to match AgingCalculator.BucketOf):
        //   NotYetDue  : dd >= today           (daysOverdue <= 0)
        //   1-30       : dd < today && dd >= d30 (1..30 days overdue)
        //   31-60      : dd < d30  && dd >= d60 (31..60)
        //   61-90      : dd < d60  && dd >= d90 (61..90)
        //   90+        : dd < d90              (> 90)
        var d30 = today.AddDays(-30);
        var d60 = today.AddDays(-60);
        var d90 = today.AddDays(-90);

        var q = from p in uow.ReadSet<Payable>()
                where p.CompanyId == companyId.Value && p.Status != PayableStatus.Cancelled && p.Status != PayableStatus.Rejected
                from i in p.Installments
                where i.Status == InstallmentStatus.Open || i.Status == InstallmentStatus.PartiallyPaid
                select new { p.VendorId, p.CounterpartyName, p.CostCenterId, i.DueDate, Remaining = i.Amount - i.PaidAmount };

        // Aggregation pushed to the database via GROUP BY + conditional SUMs (EF translates the ternary to CASE WHEN).
        var byVendorRaw = await q
            .GroupBy(x => new { x.VendorId, x.CounterpartyName })
            .Select(g => new
            {
                g.Key.VendorId,
                g.Key.CounterpartyName,
                NotYetDue = g.Sum(x => x.DueDate >= today ? x.Remaining : 0m),
                Overdue1To30 = g.Sum(x => x.DueDate < today && x.DueDate >= d30 ? x.Remaining : 0m),
                Overdue31To60 = g.Sum(x => x.DueDate < d30 && x.DueDate >= d60 ? x.Remaining : 0m),
                Overdue61To90 = g.Sum(x => x.DueDate < d60 && x.DueDate >= d90 ? x.Remaining : 0m),
                Overdue90Plus = g.Sum(x => x.DueDate < d90 ? x.Remaining : 0m),
                Total = g.Sum(x => x.Remaining),
            })
            .ToListAsync(ct);

        var byCostCenterRaw = await q
            .GroupBy(x => x.CostCenterId)
            .Select(g => new
            {
                CostCenterId = g.Key,
                NotYetDue = g.Sum(x => x.DueDate >= today ? x.Remaining : 0m),
                Overdue1To30 = g.Sum(x => x.DueDate < today && x.DueDate >= d30 ? x.Remaining : 0m),
                Overdue31To60 = g.Sum(x => x.DueDate < d30 && x.DueDate >= d60 ? x.Remaining : 0m),
                Overdue61To90 = g.Sum(x => x.DueDate < d60 && x.DueDate >= d90 ? x.Remaining : 0m),
                Overdue90Plus = g.Sum(x => x.DueDate < d90 ? x.Remaining : 0m),
                Total = g.Sum(x => x.Remaining),
            })
            .ToListAsync(ct);

        var byVendor = byVendorRaw
            .Select(r => new AgingByVendorRow(
                new VendorId(r.VendorId), r.CounterpartyName,
                r.NotYetDue, r.Overdue1To30, r.Overdue31To60, r.Overdue61To90, r.Overdue90Plus, r.Total))
            .OrderByDescending(r => r.Total)
            .ToList();

        var byCostCenter = byCostCenterRaw
            .Select(r => new AgingByCostCenterRow(
                new CostCenterId(r.CostCenterId),
                r.NotYetDue, r.Overdue1To30, r.Overdue31To60, r.Overdue61To90, r.Overdue90Plus, r.Total))
            .OrderByDescending(r => r.Total)
            .ToList();

        // Totals: aggregate the (small) vendor pivot in memory — already grouped server-side.
        var totals = new AgingBucketTotals(
            byVendor.Sum(r => r.NotYetDue),
            byVendor.Sum(r => r.Overdue1To30),
            byVendor.Sum(r => r.Overdue31To60),
            byVendor.Sum(r => r.Overdue61To90),
            byVendor.Sum(r => r.Overdue90Plus),
            byVendor.Sum(r => r.Total));

        return TypedResults.Ok(new AgingReportResponse(totals, byVendor, byCostCenter));
    }
}
