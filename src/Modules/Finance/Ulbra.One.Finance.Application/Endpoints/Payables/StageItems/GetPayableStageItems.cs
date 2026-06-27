using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Pagination;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.StageItems;

/// <summary>
/// Payable-grain list for the stage tabs (Entry / Classification / Approval / Reconciled): ONE row per
/// title, with the title total and installment count — unlike <c>ListPayables</c>, which is per-installment
/// for the operational due-date view. The payment stage stays per-installment (you pay parcelas).
/// </summary>
public sealed class GetPayableStageItems : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("stage-items", Handle).WithName("GetPayableStageItems").WithTags("Finance")
            .RequireAuthorization().Produces<PaginatedResponse<PayableStageItemResponse>>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        CompanyId companyId,
        PayableStatus? status,
        IUnitOfWork uow,
        [AsParameters] PaginationQueryParameters pagination,
        CancellationToken ct)
    {
        var q = uow.ReadSet<Payable>().Where(p => p.CompanyId == companyId.Value);
        if (status is { } st) q = q.Where(p => p.Status == st);

        // Project primitives first so the query + pagination translate to SQL; wrap typed IDs in memory.
        var rows = q.Select(p => new
        {
            p.Id,
            p.CounterpartyName,
            p.Description,
            p.CostCenterId,
            p.CategoryId,
            FirstDueDate = p.Installments.Min(i => i.DueDate),
            InstallmentCount = p.Installments.Count,
            p.TotalAmount,
            p.Status,
        });

        var page = await rows.OrderBy(x => x.FirstDueDate).ToPaginatedAsync(pagination, ct);
        return TypedResults.Ok(page.Map(x => new PayableStageItemResponse(
            new PayableId(x.Id), x.CounterpartyName, x.Description, new CostCenterId(x.CostCenterId),
            x.CategoryId == null ? null : new ExpenseCategoryId(x.CategoryId.Value),
            x.FirstDueDate, x.InstallmentCount, x.TotalAmount, x.Status)));
    }
}
