using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Contracts;

public sealed record ReceivableInstallmentResponse(
    ReceivableInstallmentId Id, int Sequence, DateOnly DueDate, decimal Amount, ReceivableInstallmentStatus Status,
    decimal ReceivedAmount, bool IsOverdue, int DaysOverdue, IReadOnlyList<ReceiptResponse> Receipts)
{
    public static ReceivableInstallmentResponse From(ReceivableInstallment i, DateOnly today)
    {
        var overdue = i.Status != ReceivableInstallmentStatus.Received && i.DueDate < today;
        return new(new ReceivableInstallmentId(i.Id), i.Sequence, i.DueDate, i.Amount, i.Status, i.ReceivedAmount,
            overdue, overdue ? today.DayNumber - i.DueDate.DayNumber : 0,
            i.Receipts.OrderBy(r => r.CreatedAt).Select(ReceiptResponse.From).ToList());
    }
}
