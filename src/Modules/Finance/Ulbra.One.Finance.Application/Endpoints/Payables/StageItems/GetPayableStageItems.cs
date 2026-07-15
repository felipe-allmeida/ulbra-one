using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
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
            .RequirePermission(Permissions.PayablesRead).Produces<PaginatedResponse<PayableStageItemResponse>>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        CompanyId companyId,
        PayableStatus? status,
        bool? hasFiscalDocument,
        PayableOrigin? origin,
        IUnitOfWork uow,
        [AsParameters] PaginationQueryParameters pagination,
        CancellationToken ct)
    {
        var q = uow.ReadSet<Payable>().Where(p => p.CompanyId == companyId.Value);
        if (status is { } st) q = q.Where(p => p.Status == st);
        // Origin filter (e.g. the "Adiantamentos" tab of Registro de Despesa: origin=Advance, no status).
        if (origin is { } og) q = q.Where(p => p.Origin == og);

        // "Has NF" = a parsed FiscalDocument (XML read at creation) OR an attached NF document (PayableAttachment —
        // the Pendências attach flow, PDF/other, no parse). Correlated EXISTS, both translated to SQL.
        var fiscalDocs = uow.ReadSet<FiscalDocument>();
        var attachments = uow.ReadSet<PayableAttachment>();

        // Project primitives first so the query + pagination translate to SQL; wrap typed IDs in memory.
        var rows = q.Select(p => new
        {
            p.Id,
            p.CounterpartyName,
            p.Description,
            p.CostCenterId,
            p.CategoryId,
            p.Origin,
            FirstDueDate = p.Installments.Min(i => i.DueDate),
            InstallmentCount = p.Installments.Count,
            p.TotalAmount,
            // Correlated scalar subquery on payable_withholding (FK-indexed); EF wraps the SUM in
            // COALESCE(..., 0), so titles without withholdings project 0. NetTotal (a computed domain
            // property that doesn't translate to SQL) is derived in the mapping below.
            TotalWithheld = p.Withholdings.Sum(w => w.Amount),
            p.Status,
            HasFiscalDocument = fiscalDocs.Any(d => d.PayableId == p.Id) || attachments.Any(a => a.PayableId == p.Id),
        });

        // Optional pendency filter (e.g. Pendências "Nota Fiscal" tab: PendingAccounting + no NF). EF
        // translates HasFiscalDocument to WHERE [NOT] EXISTS. Absent => unchanged behavior for all stages.
        // The false branch means "NF pendency", not "fact: no NF" — Advance payables (adiantamento) are
        // exempt from the NF gate, so they never count as pending. The response flag stays factual.
        if (hasFiscalDocument is { } hasNf)
            rows = hasNf
                ? rows.Where(x => x.HasFiscalDocument)
                : rows.Where(x => !x.HasFiscalDocument && x.Origin != PayableOrigin.Advance);

        var page = await rows.OrderBy(x => x.FirstDueDate).ToPaginatedAsync(pagination, ct);
        return TypedResults.Ok(page.Map(x => new PayableStageItemResponse(
            new PayableId(x.Id), x.CounterpartyName, x.Description, new CostCenterId(x.CostCenterId),
            x.CategoryId == null ? null : new ExpenseCategoryId(x.CategoryId.Value),
            x.FirstDueDate, x.InstallmentCount, x.TotalAmount, x.TotalWithheld, x.TotalAmount - x.TotalWithheld,
            x.Status, x.Origin, x.HasFiscalDocument)));
    }
}
