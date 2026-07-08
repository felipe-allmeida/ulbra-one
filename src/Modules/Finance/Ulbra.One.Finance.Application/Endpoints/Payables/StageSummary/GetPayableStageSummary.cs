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
            Reconciled: Bucket(PayableStatus.Paid)));
    }
}
