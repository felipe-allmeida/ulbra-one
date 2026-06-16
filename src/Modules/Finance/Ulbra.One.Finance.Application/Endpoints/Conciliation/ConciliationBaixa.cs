using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Conciliation;

/// <summary>
/// Shared "baixa" logic for the reconciliation actions (ConfirmMatch + MatchManually): turns a bank
/// transaction into a <see cref="Payment"/> against the installment it matches, reusing the Fatia 2 domain.
/// </summary>
internal static class ConciliationBaixa
{
    /// <summary>
    /// Registers a payment for <paramref name="txn"/> against <paramref name="installmentId"/> and reconciles the
    /// transaction. On success returns a 200 with the updated <see cref="BankTransactionResponse"/>; otherwise a Problem.
    /// </summary>
    public static async Task<IResult> ReconcileAsync(
        BankTransaction txn, Guid installmentId, string userId, IUnitOfWork uow, TimeProvider clock, CancellationToken ct)
    {
        var payable = await uow.Set<Payable>()
            .Include(p => p.Installments).ThenInclude(i => i.Payments)
            .FirstOrDefaultAsync(p => p.Installments.Any(i => i.Id == installmentId), ct);
        if (payable is null)
            return TypedResults.Problem(detail: "The installment is invalid.", statusCode: 400, title: "Match.InstallmentInvalid");

        if (!payable.IsPayable)
            return TypedResults.Problem(detail: "Only an approved payable can be reconciled.", statusCode: 400, title: "Payable.NotPayable");

        var installment = payable.FindInstallment(installmentId)!;
        var remaining = installment.Amount - installment.PaidAmount;
        if (installment.Status == InstallmentStatus.Paid || remaining != Math.Abs(txn.Amount))
            return TypedResults.Problem(detail: "The transaction amount does not match the installment's remaining amount.",
                statusCode: 400, title: "Match.AmountMismatch");

        var payment = new Payment(installment.Id, payable.Id, txn.Date, Math.Abs(txn.Amount), txn.BankAccountId,
            PaymentMethod.Other, 0, 0, 0, $"Conciliação: {txn.Description}", Guid.Parse(userId), clock.GetUtcNow());

        installment.RegisterPayment(payment);
        payable.RecomputeSettlement();
        await uow.AddAsync(payment, ct);
        txn.Reconcile(payment.Id);
        await uow.SaveChangesAsync(ct);

        return TypedResults.Ok(BankTransactionResponse.From(txn));
    }
}
