using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Pagination;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Receivables.StageItems;

/// <summary>
/// Receivable-grain list for the stage tabs: ONE row per title (with total + installment count), used by
/// the Reconciled tab. The "A Receber" baixa queue stays per-installment (you receive parcelas).
/// </summary>
/// <summary>Query-string filters for the receivable stage tabs. Bound via <c>[AsParameters]</c>.</summary>
public sealed record GetReceivableStageItemsRequest : PaginationQueryParameters
{
    [SwaggerSchema("Company that is owed. Omit for the consolidated view, which only a consolidated role "
        + "may request.")]
    public CompanyId? CompanyId { get; set; }

    [SwaggerSchema("Stage to keep — what the tab selection maps to.")]
    public ReceivableStatus? Status { get; set; }
}

public sealed class GetReceivableStageItems : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("stage-items", Handle).WithName("GetReceivableStageItems").WithTags("Finance")
            .WithSummary("Lists receivables by title for the stage tabs.")
            .WithDescription("One row per TITLE, with the total and installment count. The A Receber settlement queue "
                + "stays per installment, since receiving happens per parcela.")
            .RequirePermission(Permissions.ReceivablesRead).Produces<PaginatedResponse<ReceivableStageItemResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

    private static async Task<IResult> Handle(
        UserContext user,
        [AsParameters] GetReceivableStageItemsRequest req,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var (companyId, status) = (req.CompanyId, req.Status);
        // Consolidated "all companies" (AEL-906): companyId omitted → across every company, consolidated roles only.
        if (companyId is null && !user.CanSeeConsolidated)
            return ErrorResults.Forbidden("No access to the consolidated view.", ErrorCodes.Receivable.ConsolidatedForbidden);
        var cid = companyId?.Value;
        var q = uow.ReadSet<Receivable>().Where(r => cid == null || r.CompanyId == cid.Value);
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

        var page = await rows.OrderBy(x => x.FirstDueDate).ToPaginatedAsync(req, ct);
        return TypedResults.Ok(page.Map(x => new ReceivableStageItemResponse(
            new ReceivableId(x.Id), x.PayerName, x.Description, new CostCenterId(x.CostCenterId),
            x.FirstDueDate, x.InstallmentCount, x.TotalAmount, x.Status)));
    }
}
