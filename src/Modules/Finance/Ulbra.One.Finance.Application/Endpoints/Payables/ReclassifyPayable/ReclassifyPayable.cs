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
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.Messaging;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.ReclassifyPayable;

/// <summary>Contábil reclassifies the expense ledger account of an already-scheduled/paid payable ("Reclassificar"):
/// the Livro Razão is kept consistent by rebooking the incurral in Accounting. Publishes <see cref="PayableReclassified"/>
/// (before SaveChanges, via the outbox) → the Accounting consumer reverses+reposts the incurral (posted case) or drives
/// the parked pending (parked case). Withholdings stay locked — only the expense debit moves.</summary>
public sealed class ReclassifyPayable : IEndpoint
{
    public sealed record Request(
        [property: SwaggerSchema("New expense ledger account. This is the account the incurral will be "
            + "rebooked against.")]
        LedgerAccountId? AccountingAccountId);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/reclassify", Handle).WithName("ReclassifyPayable").WithTags("Finance")
            .WithSummary("Reclassifies an already-recorded payable.")
            .WithDescription("Moves the expense account of a payable that accounting has already recorded, "
                + "and keeps the ledger consistent by rebooking it: the original incurral is reversed and "
                + "reposted against the new account. A posting still parked in the pending queue is "
                + "redirected instead. Withholdings stay locked — only the expense debit moves. Use "
                + "classify for a payable that was never recorded.")
            .RequirePermission(Permissions.PayablesWrite).Produces<PayableResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(PayableId id, Request req,
        IUnitOfWork uow, IPublishEndpoint publish, CancellationToken ct)
    {
        var payable = await uow.Set<Payable>().FirstOrDefaultAsync(p => p.Id == id.Value, ct);
        if (payable is null) return ErrorResults.NotFound("Payable not found.", ErrorCodes.Payable.NotFound);

        if (payable.Status is not (PayableStatus.Scheduled or PayableStatus.Paid))
            return ErrorResults.InvalidState("Only a scheduled or paid payable can be reclassified.", ErrorCodes.Payable.NotReclassifiable);

        if (req.AccountingAccountId is null)
            return ErrorResults.InvalidRequest("A ledger account is required to reclassify.", ErrorCodes.LedgerAccount.Required);

        var err = await ClassificationValidation.ValidateAsync(uow, req.AccountingAccountId.Value.Value, ct);
        if (err is not null) return ErrorResults.InvalidRequest("Invalid ledger account.", err);

        // Always publishes (even for the same account) so a parked incurral self-heals — see PayableReclassification.
        await PayableReclassification.ReclassifyAndPublishAsync(payable, req.AccountingAccountId.Value.Value, uow, publish, ct);

        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(PayableResponse.From(payable));
    }
}
