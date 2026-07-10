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

namespace Ulbra.One.Finance.Application.Endpoints.Payables.StageSummary;

public sealed class GetPayableStageSummary : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("stage-summary", Handle).WithName("GetPayableStageSummary").WithTags("Finance")
            .RequireAuthorization().Produces<PayableStageSummaryResponse>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(CompanyId companyId, IUnitOfWork uow, CancellationToken ct)
    {
        // One row per non-terminal stage: COUNT of payables and SUM of their TotalAmount, grouped by Status.
        var rows = await uow.ReadSet<Payable>()
            .Where(p => p.CompanyId == companyId.Value)
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Total = g.Sum(p => p.TotalAmount) })
            .ToListAsync(ct);

        // "Nota Fiscal pendente" pendency bucket: PendingAccounting titles with no NF registered — neither a
        // parsed FiscalDocument nor an attached NF document (PayableAttachment). Separate query — the status
        // GroupBy above can't express the NOT EXISTS against the other aggregates. Advance payables
        // (adiantamento) are exempt from the NF gate, so a missing NF is not a pendency for them.
        var fiscalDocs = uow.ReadSet<FiscalDocument>();
        var attachments = uow.ReadSet<PayableAttachment>();
        var fiscalPending = await uow.ReadSet<Payable>()
            .Where(p => p.CompanyId == companyId.Value
                        && p.Status == PayableStatus.PendingAccounting
                        && p.Origin != PayableOrigin.Advance
                        && !fiscalDocs.Any(d => d.PayableId == p.Id)
                        && !attachments.Any(a => a.PayableId == p.Id))
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Total = g.Sum(p => p.TotalAmount) })
            .FirstOrDefaultAsync(ct);

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
            Payment: Bucket(PayableStatus.Scheduled),
            Reconciled: Bucket(PayableStatus.Paid),
            FiscalDocumentPending: fiscalPending is null
                ? new StageBucket(0, 0m)
                : new StageBucket(fiscalPending.Count, fiscalPending.Total)));
    }
}
