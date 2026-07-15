using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Aging;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Aging.GetAgingItems;

public sealed class GetAgingItems : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/items", Handle).WithName("GetAgingItems").WithTags("Finance")
            .RequirePermission(Permissions.PayablesRead).Produces<List<AgingItemResponse>>(StatusCodes.Status200OK);

    private const int DefaultLimit = 200;
    private const int MaxLimit = 500;

    private static async Task<IResult> Handle(
        CompanyId companyId,
        AgingBucket? bucket,
        VendorId? vendorId,
        CostCenterId? costCenterId,
        DateOnly? asOf,
        int? limit,
        IUnitOfWork uow,
        TimeProvider clock,
        CancellationToken ct)
    {
        var today = asOf ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        Guid? vid = vendorId?.Value;
        Guid? ccid = costCenterId?.Value;

        // Bucket date boundaries (match AgingCalculator.BucketOf) so the bucket filter runs in SQL, not in memory.
        var d30 = today.AddDays(-30);
        var d60 = today.AddDays(-60);
        var d90 = today.AddDays(-90);

        var query = from p in uow.ReadSet<Payable>()
                where p.CompanyId == companyId.Value && p.Status != PayableStatus.Cancelled && p.Status != PayableStatus.Rejected
                      && (vid == null || p.VendorId == vid) && (ccid == null || p.CostCenterId == ccid)
                from i in p.Installments
                where i.Status == InstallmentStatus.Open || i.Status == InstallmentStatus.PartiallyPaid
                select new
                {
                    PayableId = p.Id,
                    InstallmentId = i.Id,
                    p.VendorId,
                    p.CounterpartyName,
                    p.DocumentNumber,
                    p.CostCenterId,
                    i.DueDate,
                    Remaining = i.Amount - i.PaidAmount,
                };

        query = bucket switch
        {
            AgingBucket.NotYetDue => query.Where(x => x.DueDate >= today),
            AgingBucket.Overdue1To30 => query.Where(x => x.DueDate < today && x.DueDate >= d30),
            AgingBucket.Overdue31To60 => query.Where(x => x.DueDate < d30 && x.DueDate >= d60),
            AgingBucket.Overdue61To90 => query.Where(x => x.DueDate < d60 && x.DueDate >= d90),
            AgingBucket.Overdue90Plus => query.Where(x => x.DueDate < d90),
            _ => query,
        };

        var raw = await query
            .OrderBy(x => x.DueDate)
            .Take(take)
            .ToListAsync(ct);

        var list = raw
            .Select(r => new AgingItemResponse(
                new PayableId(r.PayableId), new InstallmentId(r.InstallmentId), new VendorId(r.VendorId),
                r.CounterpartyName, r.DocumentNumber, new CostCenterId(r.CostCenterId), r.DueDate,
                AgingCalculator.DaysOverdue(r.DueDate, today), r.Remaining, AgingCalculator.BucketOf(r.DueDate, today)))
            .ToList();

        return TypedResults.Ok(list);
    }
}
