using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.Payments;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Conciliation;

/// <summary>
/// Shared "baixa" logic for the reconciliation actions (ConfirmMatch + MatchManually): registers a
/// <see cref="Payment"/> against the matched installment through the single settlement path
/// (<see cref="IPayablePaymentService"/>), so an OFX-reconciled payment emits <c>PaymentRegistered</c>
/// (settlement accounting + Budgeting realizado) exactly like a manual baixa, then reconciles the transaction.
/// </summary>
public static class ConciliationBaixa
{
    /// <summary>
    /// Registers a payment for <paramref name="txn"/> against <paramref name="installmentId"/> and reconciles the
    /// transaction. On success returns a 200 with the updated <see cref="BankTransactionResponse"/>; otherwise a Problem.
    /// </summary>
    public static async Task<IResult> ReconcileAsync(
        BankTransaction txn, Guid installmentId, string userId,
        IUnitOfWork uow, IPayablePaymentService paymentService, CancellationToken ct)
    {
        var payable = await uow.Set<Payable>()
            .Include(p => p.Installments).ThenInclude(i => i.Payments)
            .FirstOrDefaultAsync(p => p.Installments.Any(i => i.Id == installmentId), ct);
        if (payable is null)
            return ErrorResults.InvalidRequest("The installment is invalid.", ErrorCodes.Match.InstallmentInvalid);

        var installment = payable.FindInstallment(installmentId)!;
        var remaining = installment.NetAmount - installment.PaidAmount;
        // Conciliation settles the exact remaining balance in a single baixa (partials are not reconciled here).
        if (installment.Status == InstallmentStatus.Paid || remaining != Math.Abs(txn.Amount))
            return ErrorResults.InvalidRequest("The transaction amount does not match the installment's remaining amount.", ErrorCodes.Match.AmountMismatch);

        var input = new RegisterPaymentInput(txn.Date, Math.Abs(txn.Amount), txn.BankAccountId,
            PaymentMethod.Other, Interest: 0, Fine: 0, Discount: 0, Notes: $"Conciliação: {txn.Description}");

        // Snapshot existing payment ids so we can identify the one RegisterAsync appends (to reconcile the txn to it).
        var known = installment.Payments.Select(p => p.Id).ToHashSet();
        var result = await paymentService.RegisterAsync(payable, installment, input, Guid.Parse(userId), ct);
        if (result.IsFailure)
            return result.Error.Code == ErrorCodes.Payment.InstallmentSettled
                ? ErrorResults.InvalidState(result.Error.Description, result.Error.Code)
                : ErrorResults.InvalidRequest(result.Error.Description, result.Error.Code);

        var payment = installment.Payments.First(p => !known.Contains(p.Id));
        txn.Reconcile(payment.Id);
        await uow.SaveChangesAsync(ct);

        return TypedResults.Ok(BankTransactionResponse.From(txn));
    }
}
