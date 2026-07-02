using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Core.Domain.Entities;
using Ulbra.One.Finance.Application.Messaging;
using Ulbra.One.Finance.Application.Withholdings;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.ClassifyPayableBatch;

public sealed class ClassifyPayableBatch : IEndpoint
{
    public sealed record Request(
        IReadOnlyList<PayableId> PayableIds, ChartAccountId? AccountingAccountId, ChartAccountId? FinancialAccountId);

    public sealed record Response(int ClassifiedCount);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/classify-batch", Handle).WithName("ClassifyPayableBatch").WithTags("Finance")
            .RequireAuthorization().Produces<Response>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(Request req,
        IUnitOfWork uow, IPublishEndpoint publish, TimeProvider clock, IAutoWithholdingService autoWithholding, CancellationToken ct)
    {
        var err = await ClassificationValidation.ValidateAsync(uow, req.AccountingAccountId?.Value, req.FinancialAccountId?.Value, ct);
        if (err is not null) return TypedResults.Problem(detail: "Invalid chart account.", statusCode: 400, title: err);

        var ids = req.PayableIds.Select(p => p.Value).ToList();
        var payables = await uow.Set<Payable>().Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        var now = clock.GetUtcNow();

        // Resolve the accounting code once (all payables in a batch share the same account).
        string? accountingCode = null;
        if (req.AccountingAccountId is { } acctId)
            accountingCode = await uow.ReadSet<ChartAccount>()
                .Where(c => c.Id == acctId.Value).Select(c => c.Code).FirstOrDefaultAsync(ct);

        foreach (var payable in payables)
        {
            payable.Classify(req.AccountingAccountId?.Value, req.FinancialAccountId?.Value);
            // Compute + apply withholdings before any auto-approval (they must be final before the payable schedules).
            await autoWithholding.RecalculateAndApplyAsync(payable, ct);
            var cc = await uow.ReadSet<CostCenter>().FirstOrDefaultAsync(c => c.Id == payable.CostCenterId, ct);
            payable.ApplyAutoApproval(cc?.AutoApprovalLimit ?? 0m, now);
            if (payable.Status is PayableStatus.Received or PayableStatus.PendingClassification)
                payable.MarkPendingApproval();

            if (payable.Status == PayableStatus.Scheduled)
            {
                var withholdings = payable.Withholdings.Select(w => new ScheduledWithholding(w.TaxType.ToString(), w.Amount)).ToList();
                await publish.Publish(new PayableScheduled(payable.Id, payable.CompanyId, payable.TotalAmount, accountingCode, withholdings), ct);
            }
        }

        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(new Response(payables.Count));
    }
}
