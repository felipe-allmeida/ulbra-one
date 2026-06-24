using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Pagination;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Receivables.ListReceivables;

/// <summary>Query-string filters for the Contas a Receber list. Bound via <c>[AsParameters]</c>.</summary>
public sealed record ListReceivablesRequest
{
    public CompanyId? CompanyId { get; set; }
    public ReceivableStatus? Status { get; set; }
    public CustomerId? PayerId { get; set; }
    public CostCenterId? CostCenterId { get; set; }
    public DateOnly? DueFrom { get; set; }
    public DateOnly? DueTo { get; set; }
    public bool? Received { get; set; }
}

public sealed class ListReceivables : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("", Handle).WithName("ListReceivables").WithTags("Finance")
            .RequireAuthorization().Produces<PaginatedResponse<ReceivableListItemResponse>>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        IUnitOfWork uow, TimeProvider timeProvider,
        [AsParameters] ListReceivablesRequest req,
        [AsParameters] PaginationQueryParameters pagination, CancellationToken ct)
    {
        var today = ReceivableClock.Today(timeProvider);

        var receivables = uow.ReadSet<Receivable>();
        if (req.CompanyId is { } cid) receivables = receivables.Where(p => p.CompanyId == cid.Value);
        if (req.Status is { } st) receivables = receivables.Where(p => p.Status == st);
        if (req.PayerId is { } pid) receivables = receivables.Where(p => p.PayerId == pid.Value);
        if (req.CostCenterId is { } ccid) receivables = receivables.Where(p => p.CostCenterId == ccid.Value);

        // One row per installment. Project to primitive fields FIRST so the query and pagination translate
        // to SQL (no AutoInclude tree materialized), then wrap Guids into typed IDs in memory.
        var rows = receivables.SelectMany(p => p.Installments, (p, i) => new
        {
            ReceivableId = p.Id,
            InstallmentId = i.Id,
            i.Sequence,
            InstallmentCount = p.Installments.Count,
            p.PayerName,
            p.Description,
            p.CostCenterId,
            p.RevenueCategoryId,
            i.DueDate,
            i.Amount,
            i.ReceivedAmount,
            InstallmentStatus = i.Status,
            ReceivableStatus = p.Status,
        });
        if (req.DueFrom is { } from) rows = rows.Where(x => x.DueDate >= from);
        if (req.DueTo is { } to) rows = rows.Where(x => x.DueDate <= to);
        if (req.Received is true) rows = rows.Where(x => x.InstallmentStatus == ReceivableInstallmentStatus.Received);
        if (req.Received is false) rows = rows.Where(x => x.InstallmentStatus != ReceivableInstallmentStatus.Received);

        var page = await rows.OrderBy(x => x.DueDate).ToPaginatedAsync(pagination, ct);
        return TypedResults.Ok(page.Map(x => new ReceivableListItemResponse(
            new ReceivableId(x.ReceivableId), new ReceivableInstallmentId(x.InstallmentId), x.Sequence, x.InstallmentCount,
            x.PayerName, x.Description, new CostCenterId(x.CostCenterId), x.RevenueCategoryId,
            x.DueDate, x.Amount, x.ReceivedAmount, x.InstallmentStatus, x.ReceivableStatus,
            // A terminal receivable (cancelled / written-off) has no live installments, so none are overdue.
            (x.ReceivableStatus == ReceivableStatus.AwaitingReceipt || x.ReceivableStatus == ReceivableStatus.Received)
                && x.InstallmentStatus != ReceivableInstallmentStatus.Received && x.DueDate < today)));
    }
}
