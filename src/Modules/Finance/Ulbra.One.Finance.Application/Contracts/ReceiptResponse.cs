using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Contracts;

public sealed record ReceiptResponse(
    ReceiptId Id, ReceivableInstallmentId InstallmentId, DateOnly ReceiptDate, decimal Amount, BankAccountId BankAccountId,
    PaymentMethod Method, decimal Interest, decimal Fine, decimal Discount, string? Notes, bool IsReversed,
    string? ReversalReason, DateTimeOffset? ReversedAt, bool HasProof, DateTimeOffset CreatedAt)
{
    public static ReceiptResponse From(Receipt r) => new(
        new ReceiptId(r.Id), new ReceivableInstallmentId(r.InstallmentId), r.ReceiptDate, r.Amount, new BankAccountId(r.BankAccountId),
        r.Method, r.Interest, r.Fine, r.Discount, r.Notes, r.IsReversed, r.ReversalReason, r.ReversedAt,
        r.ProofStoragePath != null, r.CreatedAt);
}
