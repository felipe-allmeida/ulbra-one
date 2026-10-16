using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Domain;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>One row of the cross-payable settlement history (Histórico de baixas).</summary>
[SwaggerSchema("One row of the settlement history (Histórico de baixas), across every payable. Reversed "
    + "payments stay in the list — the history is not rewritten when one is undone.")]
public sealed record PaymentListItemResponse(
    PaymentId PaymentId,
    PayableId PayableId,
    InstallmentId InstallmentId,
    [property: SwaggerSchema("Position of the settled installment within its payable.")]
    [property: SwaggerExample("1")]
    int InstallmentSequence,
    [property: SwaggerSchema("How many installments that payable has.")]
    [property: SwaggerExample("3")]
    int InstallmentCount,
    [property: SwaggerSchema("Vendor name as recorded on the payable.")]
    [property: SwaggerExample("Construtora Horizonte Ltda")]
    string CounterpartyName,
    /// <summary>Vendor CNPJ/CPF digits, resolved live from Registry, or null if the vendor is gone.</summary>
    [property: SwaggerSchema("Vendor CNPJ or CPF, digits only, resolved live from the registry. Null "
        + "when the vendor no longer exists there.")]
    [property: SwaggerExample("12345678000195")]
    string? VendorDocument,
    [property: SwaggerSchema("Invoice number of the payable.")]
    [property: SwaggerExample("00012345")]
    string? DocumentNumber,
    [property: SwaggerSchema("Date the money moved.")]
    [property: SwaggerExample("2026-08-08")]
    DateOnly PaymentDate,
    [property: SwaggerSchema("Principal paid, in BRL — what amortizes the installment, excluding "
        + "interest and fine.")]
    [property: SwaggerExample("1430.25")]
    decimal Amount,
    [property: SwaggerSchema("Interest paid on top, in BRL.")]
    [property: SwaggerExample("12.40")]
    decimal Interest,
    [property: SwaggerSchema("Fine paid on top, in BRL.")]
    [property: SwaggerExample("0.00")]
    decimal Fine,
    [property: SwaggerSchema("Discount obtained, in BRL.")]
    [property: SwaggerExample("0.00")]
    decimal Discount,
    [property: SwaggerSchema("How it was paid.")]
    PaymentMethod Method,
    [property: SwaggerSchema("Bank account the money left from.")]
    BankAccountId BankAccountId,
    [property: SwaggerSchema("Whether the payment was reversed. A reversed row still appears here but no "
        + "longer counts toward what the installment has paid.")]
    bool IsReversed,
    [property: SwaggerSchema("Why it was reversed.")]
    string? ReversalReason,
    [property: SwaggerSchema("Whether a proof of payment was uploaded.")]
    bool HasProof,
    [property: SwaggerSchema("When the payment was registered, which can differ from PaymentDate.")]
    DateTimeOffset CreatedAt,
    [property: SwaggerSchema("Who registered it.")]
    Guid CreatedById);
