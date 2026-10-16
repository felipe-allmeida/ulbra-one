using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Core.Domain.Entities;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.Withholdings;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.ClassifyPayable;

public sealed class ClassifyPayable : IEndpoint
{
    public sealed record Request(
        [property: SwaggerSchema("Expense ledger account to book the payable against. Null clears the "
            + "classification, sending the payable back to the unclassified queue.")]
        LedgerAccountId? AccountingAccountId);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/classify", Handle).WithName("ClassifyPayable").WithTags("Finance")
            .WithSummary("Classifies a payable.")
            .WithDescription("Sets the expense ledger account a payable is booked against — the step that "
                + "makes it postable to accounting. For a payable that was already scheduled or paid, use "
                + "reclassify instead: this one does not rebook what accounting has already recorded.")
            .RequirePermission(Permissions.PayablesWrite).Produces<PayableResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(PayableId id, Request req,
        IUnitOfWork uow, TimeProvider clock, IAutoWithholdingService autoWithholding, IPublishEndpoint publish, CancellationToken ct)
    {
        var payable = await uow.Set<Payable>().FirstOrDefaultAsync(p => p.Id == id.Value, ct);
        if (payable is null) return ErrorResults.NotFound("Payable not found.", ErrorCodes.Payable.NotFound);

        var err = await ClassificationValidation.ValidateAsync(uow, req.AccountingAccountId?.Value, ct);
        if (err is not null) return ErrorResults.InvalidRequest("Invalid ledger account.", err);

        // A payable that already scheduled has its incurral posted — the account moves through reclassify semantics
        // (rebooks the Livro Razão + resolves any parked incurral), not a silent Classify that publishes nothing.
        if (payable.Status is PayableStatus.Scheduled or PayableStatus.Paid)
        {
            if (req.AccountingAccountId is null)
                return ErrorResults.InvalidRequest("A ledger account is required to reclassify a scheduled payable.", ErrorCodes.LedgerAccount.Required);
            await PayableReclassification.ReclassifyAndPublishAsync(payable, req.AccountingAccountId.Value.Value, uow, publish, ct);
            await uow.SaveChangesAsync(ct);
            return TypedResults.Ok(PayableResponse.From(payable));
        }

        payable.Classify(req.AccountingAccountId?.Value);

        // Compute + apply withholdings during the initial classification (before approval), so they're final
        // before the payable schedules. Once it reaches the accounting stage, retentions are the accountant's to
        // correct manually — reclassifying the ledger account there must NOT recompute/overwrite them (they only
        // lock at CompleteAccounting → Scheduled).
        if (payable.Status is not PayableStatus.PendingAccounting)
            await autoWithholding.RecalculateAndApplyAsync(payable, ct);

        var cc = await uow.ReadSet<CostCenter>().FirstOrDefaultAsync(c => c.Id == payable.CostCenterId, ct);
        payable.ApplyAutoApproval(cc?.AutoApprovalLimit ?? 0m, clock.GetUtcNow());
        if (payable.Status is PayableStatus.Received or PayableStatus.PendingClassification)
            payable.MarkPendingApproval();

        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(PayableResponse.From(payable));
    }
}
