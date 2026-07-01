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

namespace Ulbra.One.Finance.Application.Endpoints.Receivables.StageItems;

/// <summary>
/// Receivable-grain list for the stage tabs: ONE row per title (with total + installment count), used by
/// the Reconciled tab. The "A Receber" baixa queue stays per-installment (you receive parcelas).
/// </summary>
public sealed class GetReceivableStageItems : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("stage-items", Handle).WithName("GetReceivableStageItems").WithTags("Finance")
            .RequireAuthorization().Produces<PaginatedResponse<ReceivableStageItemResponse>>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        CompanyId companyId,
        ReceivableStatus? status,
        IUnitOfWork uow,
        [AsParameters] PaginationQueryParameters pagination,
        CancellationToken ct)
    {
        var q = uow.ReadSet<Receivable>().Where(r => r.CompanyId == companyId.Value);
        if (status is { } st) q = q.Where(r => r.Status == st);

        var rows = q.Select(r => new
        {
            r.Id,
            r.PayerName,
            r.Description,
            r.CostCenterId,
            FirstDueDate = r.Installments.Min(i => i.DueDate),
            InstallmentCount = r.Installments.Count,
            r.TotalAmount,
            r.Status,
        });

        var page = await rows.OrderBy(x => x.FirstDueDate).ToPaginatedAsync(pagination, ct);
        return TypedResults.Ok(page.Map(x => new ReceivableStageItemResponse(
            new ReceivableId(x.Id), x.PayerName, x.Description, new CostCenterId(x.CostCenterId),
            x.FirstDueDate, x.InstallmentCount, x.TotalAmount, x.Status)));
    }
}
