using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Pagination;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.ListPayables;

/// <summary>Query-string filters for the Contas a Pagar list. Bound via <c>[AsParameters]</c>.</summary>
public sealed record ListPayablesRequest
{
    public CompanyId? CompanyId { get; set; }
    public PayableStatus? Status { get; set; }
    public VendorId? VendorId { get; set; }
    public CostCenterId? CostCenterId { get; set; }
    public ExpenseCategoryId? CategoryId { get; set; }
    public DateOnly? DueFrom { get; set; }
    public DateOnly? DueTo { get; set; }
    public bool? Paid { get; set; }
    public bool? Classified { get; set; }
}

public sealed class ListPayables : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("", Handle).WithName("ListPayables").WithTags("Finance")
            .RequireAuthorization().Produces<PaginatedResponse<PayableListItemResponse>>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        IUnitOfWork uow,
        [AsParameters] ListPayablesRequest req,
        [AsParameters] PaginationQueryParameters pagination, CancellationToken ct)
    {
        var payables = uow.ReadSet<Payable>();
        if (req.CompanyId is { } cid) payables = payables.Where(p => p.CompanyId == cid.Value);
        if (req.Status is { } st) payables = payables.Where(p => p.Status == st);
        if (req.VendorId is { } vid) payables = payables.Where(p => p.VendorId == vid.Value);
        if (req.CostCenterId is { } ccid) payables = payables.Where(p => p.CostCenterId == ccid.Value);
        if (req.CategoryId is { } catid) payables = payables.Where(p => p.CategoryId == catid.Value);
        if (req.Classified is { } cl)
            payables = payables.Where(p => (p.AccountingAccountId != null && p.FinancialAccountId != null) == cl);

        // One row per installment (the operational Contas a Pagar list). Project to primitive fields
        // FIRST so the query and pagination translate to SQL, then wrap Guids into typed IDs in memory.
        // LEFT JOIN on RemittanceLine to surface remittance id/sequence for InRemittance installments.
        var remittanceLines = uow.ReadSet<RemittanceLine>();
        var rows = payables
            .SelectMany(p => p.Installments, (p, i) => new
            {
                PayableId = p.Id,
                InstallmentId = i.Id,
                i.Sequence,
                InstallmentCount = p.Installments.Count,
                p.CounterpartyName,
                p.Description,
                p.CostCenterId,
                p.CategoryId,
                i.DueDate,
                i.Amount,
                InstallmentStatus = i.Status,
                PayableStatus = p.Status,
                i.ScheduledPaymentDate,
                i.IsHeld,
                i.HoldReason,
            })
            .GroupJoin(
                remittanceLines,
                x => x.InstallmentId,
                rl => rl.InstallmentId,
                (x, rls) => new { Row = x, Lines = rls })
            .SelectMany(
                g => g.Lines.DefaultIfEmpty(),
                (g, rl) => new
                {
                    g.Row.PayableId,
                    g.Row.InstallmentId,
                    g.Row.Sequence,
                    g.Row.InstallmentCount,
                    g.Row.CounterpartyName,
                    g.Row.Description,
                    g.Row.CostCenterId,
                    g.Row.CategoryId,
                    g.Row.DueDate,
                    g.Row.Amount,
                    g.Row.InstallmentStatus,
                    g.Row.PayableStatus,
                    g.Row.ScheduledPaymentDate,
                    g.Row.IsHeld,
                    g.Row.HoldReason,
                    RemittanceId = rl == null ? (Guid?)null : rl.RemittanceId,
                    RemittanceSequence = rl == null ? (int?)null : rl.Sequence,
                });
        if (req.DueFrom is { } from) rows = rows.Where(x => x.DueDate >= from);
        if (req.DueTo is { } to) rows = rows.Where(x => x.DueDate <= to);
        if (req.Paid is true) rows = rows.Where(x => x.InstallmentStatus == InstallmentStatus.Paid);
        if (req.Paid is false) rows = rows.Where(x => x.InstallmentStatus != InstallmentStatus.Paid);

        var page = await rows.OrderBy(x => x.DueDate).ToPaginatedAsync(pagination, ct);
        return TypedResults.Ok(page.Map(x => new PayableListItemResponse(
            new PayableId(x.PayableId), new InstallmentId(x.InstallmentId), x.Sequence, x.InstallmentCount,
            x.CounterpartyName, x.Description, new CostCenterId(x.CostCenterId),
            x.CategoryId == null ? null : new ExpenseCategoryId(x.CategoryId.Value),
            x.DueDate, x.Amount, x.InstallmentStatus, x.PayableStatus,
            x.ScheduledPaymentDate, x.IsHeld, x.HoldReason,
            x.RemittanceId == null ? null : new RemittanceId(x.RemittanceId.Value),
            x.RemittanceSequence)));
    }
}
