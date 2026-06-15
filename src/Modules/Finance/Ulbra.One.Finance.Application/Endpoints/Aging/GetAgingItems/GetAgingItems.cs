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

namespace Ulbra.One.Finance.Application.Endpoints.Aging.GetAgingItems;

public sealed class GetAgingItems : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/items", Handle).WithName("GetAgingItems").WithTags("Finance")
            .RequireAuthorization().Produces<List<AgingItemResponse>>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        CompanyId companyId,
        AgingBucket? bucket,
        VendorId? vendorId,
        CostCenterId? costCenterId,
        DateOnly? asOf,
        [FromKeyedServices("finance")] IUnitOfWork uow,
        TimeProvider clock,
        CancellationToken ct)
    {
        var today = asOf ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        Guid? vid = vendorId?.Value;
        Guid? ccid = costCenterId?.Value;

        var raw = await (from p in uow.ReadSet<Payable>()
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
                })
            .ToListAsync(ct);

        var list = raw
            .Select(r => new AgingItemResponse(
                new PayableId(r.PayableId), new InstallmentId(r.InstallmentId), new VendorId(r.VendorId),
                r.CounterpartyName, r.DocumentNumber, new CostCenterId(r.CostCenterId), r.DueDate,
                AgingCalculator.DaysOverdue(r.DueDate, today), r.Remaining, AgingCalculator.BucketOf(r.DueDate, today)))
            .Where(x => bucket == null || x.Bucket == bucket)
            .OrderBy(x => x.DueDate)
            .ToList();

        return TypedResults.Ok(list);
    }
}
