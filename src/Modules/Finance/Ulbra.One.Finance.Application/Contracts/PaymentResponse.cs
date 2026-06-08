using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Contracts;

public sealed record PaymentResponse(
    PaymentId Id, InstallmentId InstallmentId, DateOnly PaymentDate, decimal Amount, BankAccountId BankAccountId,
    PaymentMethod Method, decimal Interest, decimal Fine, decimal Discount, string? Notes, bool IsReversed,
    string? ReversalReason, bool HasProof, DateTimeOffset CreatedAt)
{
    public static PaymentResponse From(Payment p) => new(
        new PaymentId(p.Id), new InstallmentId(p.InstallmentId), p.PaymentDate, p.Amount, new BankAccountId(p.BankAccountId),
        p.Method, p.Interest, p.Fine, p.Discount, p.Notes, p.IsReversed, p.ReversalReason,
        p.ProofStoragePath != null, p.CreatedAt);
}
