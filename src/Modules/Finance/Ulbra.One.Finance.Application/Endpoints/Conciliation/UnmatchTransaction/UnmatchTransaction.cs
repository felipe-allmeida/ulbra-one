using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Conciliation.UnmatchTransaction;

public sealed class UnmatchTransaction : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/unmatch", Handle).WithName("UnmatchTransaction").WithTags("Finance")
            .WithSummary("Undoes a bank transaction match.")
            .WithDescription("Breaks the link between a statement line and its installment - the line goes back to "
                + "unmatched. What happens to the payment depends on who created it. When the match registered the "
                + "payment itself, it is reversed here; the reversal is recorded rather than erased, since payments "
                + "are append-only. When the match merely LINKED a payment that already existed (paymentPreexisting "
                + "= true, e.g. a baixa applied by the CNAB return), the line is only unlinked and the payment is "
                + "left untouched - reversing it would undo money the bank actually moved.")
            .RequirePermission(Permissions.TreasuryWrite)
            .Produces<BankTransactionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(BankTransactionId id,
        IUnitOfWork uow, CancellationToken ct)
    {
        var txn = await uow.Set<BankTransaction>().FirstOrDefaultAsync(t => t.Id == id.Value, ct);
        if (txn is null)
            return ErrorResults.NotFound("Bank transaction not found.", ErrorCodes.BankTransaction.NotFound);
        if (txn.MatchStatus != MatchStatus.Reconciled)
            return ErrorResults.InvalidState("Only a reconciled transaction can be unmatched.", ErrorCodes.BankTransaction.NotReconciled);

        // A link to a PRE-EXISTING payment (a CNAB-return baixa the conciliation merely tied to this line) is
        // undone by unlinking only. Reversing here would undo a payment the bank actually made — backing it out
        // of the requisition's realizado and reversing the settlement entry — because the conciliation was never
        // what created it. Only the conciliation-created path below reverses what it registered.
        if (txn.PaymentPreexisting)
        {
            txn.Unreconcile();
            await uow.SaveChangesAsync(ct);
            return TypedResults.Ok(BankTransactionResponse.From(txn));
        }

        var payable = await uow.Set<Payable>()
            .Include(p => p.Installments).ThenInclude(i => i.Payments)
            .FirstOrDefaultAsync(p => p.Installments.Any(i => i.Payments.Any(pay => pay.Id == txn.MatchedPaymentId)), ct);
        if (payable is null)
            return ErrorResults.NotFound("Bank transaction not found.", ErrorCodes.BankTransaction.NotFound);

        var payment = payable.FindPayment(txn.MatchedPaymentId!.Value)!;
        var installment = payable.FindInstallment(payment.InstallmentId)!;
        installment.ReversePayment(txn.MatchedPaymentId.Value, "Conciliação desfeita");
        payable.RecomputeSettlement();
        txn.Unreconcile();
        await uow.SaveChangesAsync(ct);

        return TypedResults.Ok(BankTransactionResponse.From(txn));
    }
}
