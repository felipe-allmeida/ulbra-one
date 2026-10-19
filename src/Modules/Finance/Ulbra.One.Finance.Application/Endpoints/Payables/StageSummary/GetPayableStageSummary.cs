using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.StageSummary;

public sealed class GetPayableStageSummary : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("stage-summary", Handle).WithName("GetPayableStageSummary").WithTags("Finance")
            .WithSummary("Counts payables per workflow stage.")
            .WithDescription("The counters behind the stage tabs - how many titles sit in entry, classification, approval "
                + "and so on. Counts TITLES, not installments, matching what the stage list returns.")
            .RequirePermission(Permissions.PayablesRead).Produces<PayableStageSummaryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

    private static async Task<IResult> Handle(
        [SwaggerSchema("Company to count. Omit for the consolidated view across every company, which only "
            + "a consolidated role may request.")]
        CompanyId? companyId, UserContext user, IUnitOfWork uow, CancellationToken ct)
    {
        // "All companies" (AEL-906): companyId omitted → aggregate across every company; consolidated roles only.
        if (companyId is null && !user.CanSeeConsolidated)
            return ErrorResults.Forbidden("No access to the consolidated view.", ErrorCodes.Payable.ConsolidatedForbidden);
        var cid = companyId?.Value;

        // One row per non-terminal stage: COUNT of payables and SUM of their TotalAmount, grouped by Status.
        var rows = await uow.ReadSet<Payable>()
            .Where(p => (cid == null || p.CompanyId == cid.Value))
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Total = g.Sum(p => p.TotalAmount) })
            .ToListAsync(ct);

        // "Documento pendente" bucket: no qualifying fiscal document on PendingTaxAnalysis/PendingAccounting/
        // Scheduled/Paid — a parsed FiscalDocument or a Nota Fiscal attachment, and for Conta Fixa
        // (RequiresFiscalDocument) a Fatura also clears it (Fatura OU NF, mirrors PayableFiscalGate). A plain
        // boleto/quote never clears. Advance is exempt. Separate query (the GroupBy above can't express the
        // NOT EXISTS against the other aggregates).
        var fiscalDocs = uow.ReadSet<FiscalDocument>();
        var attachments = uow.ReadSet<PayableAttachment>();
        // Grouped by "is it still in the accounting stage?" so ONE query yields both buckets: the wide one
        // (both groups) and the accounting-scoped one (the true group). The scoped bucket is what the
        // Pendencias "Nota Fiscal" tab badges - that tab lists PendingAccounting only, so badging it with the
        // wide bucket showed a count with nothing behind it (a tax-analysis/scheduled/paid title has its NF
        // chased elsewhere and is not attachable there).
        var fiscalPendingRows = await uow.ReadSet<Payable>()
            .Where(p => (cid == null || p.CompanyId == cid.Value)
                        && (p.Status == PayableStatus.PendingTaxAnalysis
                            || p.Status == PayableStatus.PendingAccounting
                            || p.Status == PayableStatus.Scheduled
                            || p.Status == PayableStatus.Paid)
                        && p.Origin != PayableOrigin.Advance
                        && !fiscalDocs.Any(d => d.PayableId == p.Id)
                        && !attachments.Any(a => a.PayableId == p.Id && a.Kind == PayableAttachmentKind.FiscalDocument)
                        && !(p.RequiresFiscalDocument && attachments.Any(a => a.PayableId == p.Id && a.Kind == PayableAttachmentKind.Invoice)))
            .GroupBy(p => p.Status == PayableStatus.PendingAccounting)
            .Select(g => new { InAccounting = g.Key, Count = g.Count(), Total = g.Sum(p => p.TotalAmount) })
            .ToListAsync(ct);

        // Net sum for the Payment bucket only: what will actually leave the bank. Σ Installment.NetAmount
        // == NetTotal per payable (invariant kept by RecomputeInstallmentNetAmounts), so a flat SUM over
        // the scheduled installments is exact — and it's literally what the remittance pays. Separate
        // scalar query: a nested Sum inside the status GroupBy above doesn't translate.
        var paymentNetTotal = await uow.ReadSet<Payable>()
            .Where(p => (cid == null || p.CompanyId == cid.Value) && p.Status == PayableStatus.Scheduled)
            .SelectMany(p => p.Installments)
            .SumAsync(i => i.NetAmount, ct);

        StageBucket FiscalBucket(bool inAccounting)
        {
            var r = fiscalPendingRows.FirstOrDefault(x => x.InAccounting == inAccounting);
            return r is null ? new StageBucket(0, 0m) : new StageBucket(r.Count, r.Total);
        }

        StageBucket Bucket(PayableStatus s)
        {
            var r = rows.FirstOrDefault(x => x.Status == s);
            return r is null ? new StageBucket(0, 0m) : new StageBucket(r.Count, r.Total);
        }

        return TypedResults.Ok(new PayableStageSummaryResponse(
            Entry: Bucket(PayableStatus.Received),
            Classification: Bucket(PayableStatus.PendingClassification),
            Approval: Bucket(PayableStatus.PendingApproval),
            Accounting: Bucket(PayableStatus.PendingAccounting),
            TaxAnalysis: Bucket(PayableStatus.PendingTaxAnalysis),
            Payment: Bucket(PayableStatus.Scheduled) with { NetTotalAmount = paymentNetTotal },
            Reconciled: Bucket(PayableStatus.Paid),
            FiscalDocumentPending: new StageBucket(
                fiscalPendingRows.Sum(x => x.Count), fiscalPendingRows.Sum(x => x.Total)),
            FiscalDocumentPendingAccounting: FiscalBucket(inAccounting: true)));
    }
}
