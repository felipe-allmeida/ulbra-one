using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Core.Domain.Entities;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.Withholdings;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.ClassifyPayable;

public sealed class ClassifyPayable : IEndpoint
{
    public sealed record Request(LedgerAccountId? AccountingAccountId);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/classify", Handle).WithName("ClassifyPayable").WithTags("Finance")
            .RequirePermission(Permissions.PayablesWrite).Produces<PayableResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(PayableId id, Request req,
        IUnitOfWork uow, TimeProvider clock, IAutoWithholdingService autoWithholding, CancellationToken ct)
    {
        var payable = await uow.Set<Payable>().FirstOrDefaultAsync(p => p.Id == id.Value, ct);
        if (payable is null) return TypedResults.Problem(detail: "Payable not found.", statusCode: 404, title: "Payable.NotFound");

        var err = await ClassificationValidation.ValidateAsync(uow, req.AccountingAccountId?.Value, ct);
        if (err is not null) return TypedResults.Problem(detail: "Invalid ledger account.", statusCode: 400, title: err);

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
