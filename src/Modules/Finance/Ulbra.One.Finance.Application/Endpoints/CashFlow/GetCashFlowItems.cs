using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.CashFlow;

/// <summary>
/// Drill-down: the open installments composing a cash-flow period bucket for one direction.
/// Σ amount must equal the corresponding bucket value (covered by an integration test).
/// </summary>
public sealed class GetCashFlowItems : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/items", Handle).WithName("GetCashFlowItems").WithTags("Finance")
            .RequireAuthorization().Produces<List<CashFlowItemResponse>>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        CompanyId? companyId,
        DateOnly from,
        DateOnly to,
        string direction,
        DateOnly? asOf,
        IUnitOfWork uow,
        TimeProvider clock,
        CancellationToken ct)
    {
        var today = asOf ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        Guid? cid = companyId?.Value;
        var isOutflow = string.Equals(direction, "out", StringComparison.OrdinalIgnoreCase);
        var windowStart = from;
        var windowEnd = to;

        // The current (first) period folds overdue items: for outflows, installments due before the
        // window start also belong here when the window contains AsOf.
        var isCurrentPeriod = windowStart <= today && today <= windowEnd;
        var includeOverdueOut = isOutflow && isCurrentPeriod;

        List<CashFlowItemResponse> items;
        if (isOutflow)
        {
            var q = from p in uow.ReadSet<Payable>()
                    where (cid == null || p.CompanyId == cid)
                          && p.Status != PayableStatus.Cancelled && p.Status != PayableStatus.Rejected
                    from i in p.Installments
                    where (i.Status == InstallmentStatus.Open || i.Status == InstallmentStatus.PartiallyPaid)
                          && i.DueDate <= windowEnd
                          && (i.DueDate >= windowStart || (includeOverdueOut && i.DueDate < today))
                    orderby i.DueDate
                    select new CashFlowItemResponse(
                        "payable", p.Id, i.Id, i.Sequence, p.CounterpartyName, p.DocumentNumber,
                        i.DueDate, i.Amount - i.PaidAmount, i.Status.ToString());
            items = await q.ToListAsync(ct);
        }
        else
        {
            // Receivables: overdue inflows are excluded entirely from the projection, so the drill
            // only ever lists installments due within [from, to] (never before AsOf).
            var lower = windowStart < today ? today : windowStart;
            var q = from r in uow.ReadSet<Receivable>()
                    where (cid == null || r.CompanyId == cid)
                          && r.Status != ReceivableStatus.Cancelled && r.Status != ReceivableStatus.WrittenOff
                    from i in r.Installments
                    where (i.Status == ReceivableInstallmentStatus.Open || i.Status == ReceivableInstallmentStatus.PartiallyReceived)
                          && i.DueDate >= lower && i.DueDate <= windowEnd
                    orderby i.DueDate
                    select new CashFlowItemResponse(
                        "receivable", r.Id, i.Id, i.Sequence, r.PayerName, r.DocumentNumber,
                        i.DueDate, i.Amount - i.ReceivedAmount, i.Status.ToString());
            items = await q.ToListAsync(ct);
        }

        return TypedResults.Ok(items);
    }
}
