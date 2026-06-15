using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Aging;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Aging.GetAgingReport;

public sealed class GetAgingReport : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("", Handle).WithName("GetAgingReport").WithTags("Finance")
            .RequireAuthorization().Produces<AgingReportResponse>(StatusCodes.Status200OK);

    private sealed record Row(Guid VendorId, string CounterpartyName, Guid CostCenterId, DateOnly DueDate, decimal Remaining, AgingBucket Bucket);

    private static async Task<IResult> Handle(
        CompanyId companyId,
        DateOnly? asOf,
        [FromKeyedServices("finance")] IUnitOfWork uow,
        TimeProvider clock,
        CancellationToken ct)
    {
        var today = asOf ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        var raw = await (from p in uow.ReadSet<Payable>()
                where p.CompanyId == companyId.Value && p.Status != PayableStatus.Cancelled && p.Status != PayableStatus.Rejected
                from i in p.Installments
                where i.Status == InstallmentStatus.Open || i.Status == InstallmentStatus.PartiallyPaid
                select new { p.VendorId, p.CounterpartyName, p.CostCenterId, i.DueDate, Remaining = i.Amount - i.PaidAmount })
            .ToListAsync(ct);

        var rows = raw
            .Select(r => new Row(r.VendorId, r.CounterpartyName, r.CostCenterId, r.DueDate, r.Remaining, AgingCalculator.BucketOf(r.DueDate, today)))
            .ToList();

        static decimal SumBucket(IEnumerable<Row> g, AgingBucket b) => g.Where(r => r.Bucket == b).Sum(r => r.Remaining);

        var totals = new AgingBucketTotals(
            SumBucket(rows, AgingBucket.NotYetDue),
            SumBucket(rows, AgingBucket.Overdue1To30),
            SumBucket(rows, AgingBucket.Overdue31To60),
            SumBucket(rows, AgingBucket.Overdue61To90),
            SumBucket(rows, AgingBucket.Overdue90Plus),
            rows.Sum(r => r.Remaining));

        var byVendor = rows
            .GroupBy(r => r.VendorId)
            .Select(g => new AgingByVendorRow(
                new VendorId(g.Key), g.First().CounterpartyName,
                SumBucket(g, AgingBucket.NotYetDue), SumBucket(g, AgingBucket.Overdue1To30),
                SumBucket(g, AgingBucket.Overdue31To60), SumBucket(g, AgingBucket.Overdue61To90),
                SumBucket(g, AgingBucket.Overdue90Plus), g.Sum(r => r.Remaining)))
            .OrderByDescending(r => r.Total)
            .ToList();

        var byCostCenter = rows
            .GroupBy(r => r.CostCenterId)
            .Select(g => new AgingByCostCenterRow(
                new CostCenterId(g.Key),
                SumBucket(g, AgingBucket.NotYetDue), SumBucket(g, AgingBucket.Overdue1To30),
                SumBucket(g, AgingBucket.Overdue31To60), SumBucket(g, AgingBucket.Overdue61To90),
                SumBucket(g, AgingBucket.Overdue90Plus), g.Sum(r => r.Remaining)))
            .OrderByDescending(r => r.Total)
            .ToList();

        return TypedResults.Ok(new AgingReportResponse(totals, byVendor, byCostCenter));
    }
}
