using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Core.Domain.Entities;
using Ulbra.One.Finance.Application.Withholdings;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.ClassifyPayableBatch;

public sealed class ClassifyPayableBatch : IEndpoint
{
    public sealed record Request(
        [property: SwaggerSchema("Payables to classify in one call. All of them get the same account.")]
        IReadOnlyList<PayableId> PayableIds,
        [property: SwaggerSchema("Expense ledger account to apply to every payable in the list. Null "
            + "clears their classification.")]
        LedgerAccountId? AccountingAccountId);

    [SwaggerSchema("Outcome of a batch classification.")]
    public sealed record Response(
        [property: SwaggerSchema("How many payables were classified. Payables the classifier could not "
            + "decide on are left untouched rather than guessed at, so this can be lower than the number "
            + "sent.")]
        [property: SwaggerExample("18")]
        int ClassifiedCount);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/classify-batch", Handle).WithName("ClassifyPayableBatch").WithTags("Finance")
            .WithSummary("Classifies several payables at once.")
            .WithDescription("Applies the same expense ledger account to a list of payables and returns "
                + "how many were classified — the count can be lower than the list when some were not "
                + "eligible. Like the single-payable version, it does not rebook payables already recorded "
                + "in accounting.")
            .RequirePermission(Permissions.PayablesWrite).Produces<Response>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(Request req,
        IUnitOfWork uow, TimeProvider clock, IAutoWithholdingService autoWithholding, IPublishEndpoint publish, CancellationToken ct)
    {
        var err = await ClassificationValidation.ValidateAsync(uow, req.AccountingAccountId?.Value, ct);
        if (err is not null) return ErrorResults.InvalidRequest("Invalid ledger account.", err);

        var ids = req.PayableIds.Select(p => p.Value).ToList();
        var payables = await uow.Set<Payable>().Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        var now = clock.GetUtcNow();

        // A scheduled/paid payable must be reclassified (rebooks the Livro Razão), which requires a concrete account.
        if (req.AccountingAccountId is null && payables.Any(p => p.Status is PayableStatus.Scheduled or PayableStatus.Paid))
            return ErrorResults.InvalidRequest("A ledger account is required to reclassify a scheduled payable.", ErrorCodes.LedgerAccount.Required);

        foreach (var payable in payables)
        {
            if (payable.Status is PayableStatus.Scheduled or PayableStatus.Paid)
            {
                await PayableReclassification.ReclassifyAndPublishAsync(payable, req.AccountingAccountId!.Value.Value, uow, publish, ct);
                continue;
            }

            payable.Classify(req.AccountingAccountId?.Value);
            // Compute + apply withholdings before any auto-approval (they must be final before the payable schedules).
            await autoWithholding.RecalculateAndApplyAsync(payable, ct);
            var cc = await uow.ReadSet<CostCenter>().FirstOrDefaultAsync(c => c.Id == payable.CostCenterId, ct);
            payable.ApplyAutoApproval(cc?.AutoApprovalLimit ?? 0m, now);
            if (payable.Status is PayableStatus.Received or PayableStatus.PendingClassification)
                payable.MarkPendingApproval();
        }

        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(new Response(payables.Count));
    }
}
