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

public sealed class ListPayables : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("", Handle).WithName("ListPayables").WithTags("Finance")
            .RequireAuthorization().Produces<PaginatedResponse<PayableListItemResponse>>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        [FromKeyedServices("finance")] IUnitOfWork uow,
        CompanyId? companyId, PayableStatus? status, VendorId? vendorId, CostCenterId? costCenterId,
        ExpenseCategoryId? categoryId, DateOnly? dueFrom, DateOnly? dueTo,
        [AsParameters] PaginationQueryParameters pagination, CancellationToken ct)
    {
        var payables = uow.ReadSet<Payable>();
        if (companyId is { } cid) payables = payables.Where(p => p.CompanyId == cid.Value);
        if (status is { } st) payables = payables.Where(p => p.Status == st);
        if (vendorId is { } vid) payables = payables.Where(p => p.VendorId == vid.Value);
        if (costCenterId is { } ccid) payables = payables.Where(p => p.CostCenterId == ccid.Value);
        if (categoryId is { } catid) payables = payables.Where(p => p.CategoryId == catid.Value);

        // One row per installment (the operational Contas a Pagar list). Project to primitive fields
        // FIRST so the query and pagination translate to SQL, then wrap Guids into typed IDs in memory.
        var rows = payables.SelectMany(p => p.Installments, (p, i) => new
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
        });
        if (dueFrom is { } from) rows = rows.Where(x => x.DueDate >= from);
        if (dueTo is { } to) rows = rows.Where(x => x.DueDate <= to);

        var page = await rows.OrderBy(x => x.DueDate).ToPaginatedAsync(pagination, ct);
        return TypedResults.Ok(page.Map(x => new PayableListItemResponse(
            new PayableId(x.PayableId), new InstallmentId(x.InstallmentId), x.Sequence, x.InstallmentCount,
            x.CounterpartyName, x.Description, new CostCenterId(x.CostCenterId),
            x.CategoryId == null ? null : new ExpenseCategoryId(x.CategoryId.Value),
            x.DueDate, x.Amount, x.InstallmentStatus, x.PayableStatus)));
    }
}
