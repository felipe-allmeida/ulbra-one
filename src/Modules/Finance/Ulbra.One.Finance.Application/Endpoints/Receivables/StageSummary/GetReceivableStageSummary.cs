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

namespace Ulbra.One.Finance.Application.Endpoints.Receivables.StageSummary;

public sealed class GetReceivableStageSummary : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("stage-summary", Handle).WithName("GetReceivableStageSummary").WithTags("Finance")
            .WithSummary("Counts receivables per workflow stage.")
            .WithDescription("The counters behind the stage tabs. Counts TITLES, not installments, matching what the stage "
                + "list returns.")
            .RequirePermission(Permissions.ReceivablesRead).Produces<ReceivableStageSummaryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

    private static async Task<IResult> Handle(
        [SwaggerSchema("Company to count. Omit for the consolidated view, which only a consolidated role "
            + "may request.")]
        CompanyId? companyId, UserContext user, IUnitOfWork uow, CancellationToken ct)
    {
        // "All companies" (AEL-906): companyId omitted → aggregate across every company; consolidated roles only.
        if (companyId is null && !user.CanSeeConsolidated)
            return ErrorResults.Forbidden("No access to the consolidated view.", ErrorCodes.Receivable.ConsolidatedForbidden);
        var cid = companyId?.Value;

        // Reconciled: simple count/sum over Received.
        var reconciled = await uow.ReadSet<Receivable>()
            .Where(r => (cid == null || r.CompanyId == cid.Value) && r.Status == ReceivableStatus.Received)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Total = g.Sum(r => r.TotalAmount) })
            .FirstOrDefaultAsync(ct);

        // AwaitingReceipt split by whether ANY installment already has a received amount.
        var awaiting = await uow.ReadSet<Receivable>()
            .Where(r => (cid == null || r.CompanyId == cid.Value) && r.Status == ReceivableStatus.AwaitingReceipt)
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
