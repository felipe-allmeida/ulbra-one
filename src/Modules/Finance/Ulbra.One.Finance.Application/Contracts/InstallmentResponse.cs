using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Contracts;

public sealed record InstallmentResponse(
    InstallmentId Id, int Sequence, DateOnly DueDate, decimal Amount, InstallmentStatus Status, decimal PaidAmount,
    IReadOnlyList<PaymentResponse> Payments, decimal NetAmount)
{
    public static InstallmentResponse From(Installment i) =>
        new(new InstallmentId(i.Id), i.Sequence, i.DueDate, i.Amount, i.Status, i.PaidAmount,
            i.Payments.OrderBy(p => p.CreatedAt).Select(PaymentResponse.From).ToList(),
            i.NetAmount);
}
