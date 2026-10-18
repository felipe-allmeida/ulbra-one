using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Contracts;

[SwaggerSchema("One installment of a receivable, with its overdue state computed against today.")]
public sealed record ReceivableInstallmentResponse(
    ReceivableInstallmentId Id,
    [property: SwaggerSchema("Position within the receivable, starting at 1.")]
    [property: SwaggerExample("1")]
    int Sequence,
    [property: SwaggerSchema("When the installment falls due.")]
    [property: SwaggerExample("2026-08-10")]
    DateOnly DueDate,
    [property: SwaggerSchema("Amount owed on this installment, in BRL.")]
    [property: SwaggerExample("6000.00")]
    decimal Amount,
    [property: SwaggerSchema("Where the installment stands.")]
    ReceivableInstallmentStatus Status,
    [property: SwaggerSchema("Received so far, in BRL — the sum of non-reversed receipts.")]
    [property: SwaggerExample("6000.00")]
    decimal ReceivedAmount,
    [property: SwaggerSchema("Whether the installment is past due and still open. Always false on a "
        + "cancelled or written-off receivable — a terminal title has no live installments to be late.")]
    bool IsOverdue,
    [property: SwaggerSchema("Calendar days past the due date. Zero when not overdue.")]
    [property: SwaggerExample("12")]
    int DaysOverdue,
    [property: SwaggerSchema("Receipts against this installment, oldest first.")]
    IReadOnlyList<ReceiptResponse> Receipts)
{
    public static ReceivableInstallmentResponse From(ReceivableInstallment i, DateOnly today, bool receivableIsActive)
    {
        // A terminal receivable (cancelled / written-off) has no live installments, so none are overdue.
        var overdue = receivableIsActive && i.Status != ReceivableInstallmentStatus.Received && i.DueDate < today;
        return new(new ReceivableInstallmentId(i.Id), i.Sequence, i.DueDate, i.Amount, i.Status, i.ReceivedAmount,
            overdue, overdue ? today.DayNumber - i.DueDate.DayNumber : 0,
            i.Receipts.OrderBy(r => r.CreatedAt).Select(ReceiptResponse.From).ToList());
    }
}
