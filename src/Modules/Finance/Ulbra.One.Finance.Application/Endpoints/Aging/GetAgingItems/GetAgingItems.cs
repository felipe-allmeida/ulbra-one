using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Application.Aging;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Aging.GetAgingItems;

public sealed class GetAgingItems : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/items", Handle).WithName("GetAgingItems").WithTags("Finance")
            .WithSummary("Lists the installments behind an aging bucket.")
            .WithDescription("Drill-down into the aging report: the individual overdue installments, so a bucket total can "
                + "be explained. Capped at 500 rows - it is a drill-down, not an export.")
            .RequirePermission(Permissions.PayablesRead).Produces<List<AgingItemResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

    private const int DefaultLimit = 200;
    private const int MaxLimit = 500;

    private static async Task<IResult> Handle(
        [SwaggerSchema("Company to drill into. Omit for the consolidated view, which only a consolidated "
            + "role may request.")]
        CompanyId? companyId,
        UserContext user,
        [SwaggerSchema("Aging bucket to list. Omit to include every bucket.")]
        AgingBucket? bucket,
        [SwaggerSchema("Vendor to be paid.")]
        VendorId? vendorId,
        [SwaggerSchema("Cost center the expense is charged to.")]
        CostCenterId? costCenterId,
        [SwaggerSchema("Date the aging is measured against. Omitted uses today; pass the same value used "
            + "for the report so the numbers agree.")]
        [SwaggerExample("2026-09-30")]
        DateOnly? asOf,
        [SwaggerSchema("How many rows to return. Defaults to 200 and is capped at 500.")]
        [SwaggerExample("200")]
        int? limit,
        IUnitOfWork uow,
        TimeProvider clock,
        CancellationToken ct)
    {
        // Consolidated "all companies" (AEL-906): companyId omitted → across every company, consolidated roles only.
        if (companyId is null && !user.CanSeeConsolidated)
            return ErrorResults.Forbidden("No access to the consolidated view.", ErrorCodes.Payable.ConsolidatedForbidden);
        var cid = companyId?.Value;
        var today = asOf ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        Guid? vid = vendorId?.Value;
        Guid? ccid = costCenterId?.Value;

        // Bucket date boundaries (match AgingCalculator.BucketOf) so the bucket filter runs in SQL, not in memory.
        var d30 = today.AddDays(-30);
        var d60 = today.AddDays(-60);
        var d90 = today.AddDays(-90);

        var query = from p in uow.ReadSet<Payable>()
                where (cid == null || p.CompanyId == cid.Value) && p.Status != PayableStatus.Cancelled && p.Status != PayableStatus.Rejected
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
