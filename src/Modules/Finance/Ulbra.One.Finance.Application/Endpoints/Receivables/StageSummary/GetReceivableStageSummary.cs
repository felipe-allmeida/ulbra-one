using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Receivables.StageSummary;

public sealed class GetReceivableStageSummary : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("stage-summary", Handle).WithName("GetReceivableStageSummary").WithTags("Finance")
            .RequirePermission(Permissions.ReceivablesRead).Produces<ReceivableStageSummaryResponse>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(CompanyId companyId, IUnitOfWork uow, CancellationToken ct)
    {
        // Reconciled: simple count/sum over Received.
        var reconciled = await uow.ReadSet<Receivable>()
            .Where(r => r.CompanyId == companyId.Value && r.Status == ReceivableStatus.Received)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Total = g.Sum(r => r.TotalAmount) })
            .FirstOrDefaultAsync(ct);

        // AwaitingReceipt split by whether ANY installment already has a received amount.
        var awaiting = await uow.ReadSet<Receivable>()
            .Where(r => r.CompanyId == companyId.Value && r.Status == ReceivableStatus.AwaitingReceipt)
            .Select(r => new { r.TotalAmount, HasReceipt = r.Installments.Any(i => i.ReceivedAmount > 0m) })
            .ToListAsync(ct);

        var entry = awaiting.Where(x => !x.HasReceipt).ToList();
        var receipt = awaiting.Where(x => x.HasReceipt).ToList();

        return TypedResults.Ok(new ReceivableStageSummaryResponse(
            Entry: new StageBucket(entry.Count, entry.Sum(x => x.TotalAmount)),
            Receipt: new StageBucket(receipt.Count, receipt.Sum(x => x.TotalAmount)),
            Reconciled: reconciled is null ? new StageBucket(0, 0m) : new StageBucket(reconciled.Count, reconciled.Total)));
    }
}
