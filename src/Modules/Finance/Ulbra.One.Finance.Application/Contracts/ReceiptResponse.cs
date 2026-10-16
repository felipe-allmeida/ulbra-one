using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Contracts;

[SwaggerSchema("A receipt against a receivable installment — money in. Like payments, receipts are "
    + "reversed rather than deleted.")]
public sealed record ReceiptResponse(
    ReceiptId Id,
    [property: SwaggerSchema("Installment this receipt settles.")]
    ReceivableInstallmentId InstallmentId,
    [property: SwaggerSchema("Date the money arrived.")]
    [property: SwaggerExample("2026-08-09")]
    DateOnly ReceiptDate,
    [property: SwaggerSchema("Principal received, in BRL — what amortizes the installment, excluding "
        + "interest and fine.")]
    [property: SwaggerExample("6000.00")]
    decimal Amount,
    [property: SwaggerSchema("Bank account the money arrived in.")]
    BankAccountId BankAccountId,
    [property: SwaggerSchema("How it was received.")]
    PaymentMethod Method,
    [property: SwaggerSchema("Interest charged on top, in BRL.")]
    [property: SwaggerExample("48.00")]
    decimal Interest,
    [property: SwaggerSchema("Fine charged on top, in BRL.")]
    [property: SwaggerExample("0.00")]
    decimal Fine,
    [property: SwaggerSchema("Discount granted, in BRL.")]
    [property: SwaggerExample("0.00")]
    decimal Discount,
    [property: SwaggerSchema("Free-text note recorded with the receipt.")]
    string? Notes,
    [property: SwaggerSchema("Whether the receipt was reversed. It stays in the list and stops counting.")]
    bool IsReversed,
    [property: SwaggerSchema("Why it was reversed.")]
    string? ReversalReason,
    [property: SwaggerSchema("When it was reversed.")]
    DateTimeOffset? ReversedAt,
    [property: SwaggerSchema("Whether a proof was uploaded.")]
    bool HasProof,
    [property: SwaggerSchema("When the receipt was registered, which can differ from ReceiptDate.")]
    DateTimeOffset CreatedAt)
{
    public static ReceiptResponse From(Receipt r) => new(
        new ReceiptId(r.Id), new ReceivableInstallmentId(r.InstallmentId), r.ReceiptDate, r.Amount, new BankAccountId(r.BankAccountId),
        r.Method, r.Interest, r.Fine, r.Discount, r.Notes, r.IsReversed, r.ReversalReason, r.ReversedAt,
        r.ProofStoragePath != null, r.CreatedAt);
}
