using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Pagination;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Receivables.ListReceivables;

/// <summary>Query-string filters for the Contas a Receber list. Bound via <c>[AsParameters]</c>.</summary>
public sealed record ListReceivablesRequest : PaginationQueryParameters
{
    [SwaggerSchema("Company that is owed. Omit for the consolidated view, which only a consolidated "
        + "role may request.")]
    public CompanyId? CompanyId { get; set; }

    [SwaggerSchema("Lifecycle stage to keep.")]
    public ReceivableStatus? Status { get; set; }

    [SwaggerSchema("Customer who owes it.")]
    public CustomerId? PayerId { get; set; }

    [SwaggerSchema("Cost center the revenue is credited to.")]
    public CostCenterId? CostCenterId { get; set; }

    [SwaggerSchema("Keep installments due on or after this day. Rows are per INSTALLMENT here.")]
    [SwaggerExample("2026-09-01")]
    public DateOnly? DueFrom { get; set; }

    [SwaggerSchema("Keep installments due on or before this day.")]
    [SwaggerExample("2026-09-30")]
    public DateOnly? DueTo { get; set; }

    [SwaggerSchema("Keep only settled or only outstanding installments.")]
    public bool? Received { get; set; }
}

public sealed class ListReceivables : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("", Handle).WithName("ListReceivables").WithTags("Finance")
            .WithSummary("Lists receivables by installment.")
            .WithDescription("The operational Contas a Receber view: one row per INSTALLMENT, not per title, because "
                + "receiving happens per parcela. For one row per title use stage-items instead.")
            .RequirePermission(Permissions.ReceivablesRead).Produces<PaginatedResponse<ReceivableListItemResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

    private static async Task<IResult> Handle(
        IUnitOfWork uow, TimeProvider timeProvider,
        UserContext user,
        [AsParameters] ListReceivablesRequest req, CancellationToken ct)
    {
        // Consolidated "all companies" (AEL-906): companyId omitted → across every company, consolidated roles only.
        if (req.CompanyId is null && !user.CanSeeConsolidated)
            return ErrorResults.Forbidden("No access to the consolidated view.", ErrorCodes.Receivable.ConsolidatedForbidden);

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

        var page = await rows.OrderBy(x => x.DueDate).ToPaginatedAsync(req, ct);
        return TypedResults.Ok(page.Map(x => new ReceivableListItemResponse(
            new ReceivableId(x.ReceivableId), new ReceivableInstallmentId(x.InstallmentId), x.Sequence, x.InstallmentCount,
            x.PayerName, x.Description, new CostCenterId(x.CostCenterId), x.RevenueCategoryId,
            x.DueDate, x.Amount, x.ReceivedAmount, x.InstallmentStatus, x.ReceivableStatus,
            // A terminal receivable (cancelled / written-off) has no live installments, so none are overdue.
            (x.ReceivableStatus == ReceivableStatus.AwaitingReceipt || x.ReceivableStatus == ReceivableStatus.Received)
                && x.InstallmentStatus != ReceivableInstallmentStatus.Received && x.DueDate < today)));
    }
}
