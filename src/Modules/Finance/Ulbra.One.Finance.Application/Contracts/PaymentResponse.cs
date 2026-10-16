using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Contracts;

[SwaggerSchema("A payment made against an installment. Payments are never deleted — a mistake is undone "
    + "by reversing, which keeps the row and stops it counting.")]
public sealed record PaymentResponse(
    PaymentId Id,
    [property: SwaggerSchema("Installment this payment settles.")]
    InstallmentId InstallmentId,
    [property: SwaggerSchema("Date the money moved — the competence date of the settlement posting.")]
    [property: SwaggerExample("2026-08-08")]
    DateOnly PaymentDate,
    [property: SwaggerSchema("Principal paid, in BRL. Excludes interest and fine and is already net of "
        + "any discount, so it is what amortizes the installment rather than what left the account.")]
    [property: SwaggerExample("1430.25")]
    decimal Amount,
    [property: SwaggerSchema("Bank account the money left from.")]
    BankAccountId BankAccountId,
    [property: SwaggerSchema("How it was paid.")]
    PaymentMethod Method,
    [property: SwaggerSchema("Interest paid on top, in BRL. Does not amortize the installment.")]
    [property: SwaggerExample("12.40")]
    decimal Interest,
    [property: SwaggerSchema("Fine paid on top, in BRL. Does not amortize the installment either.")]
    [property: SwaggerExample("0.00")]
    decimal Fine,
    [property: SwaggerSchema("Discount obtained, in BRL — it reduces what had to be paid to settle.")]
    [property: SwaggerExample("0.00")]
    decimal Discount,
    [property: SwaggerSchema("Free-text note recorded with the payment.")]
    string? Notes,
    [property: SwaggerSchema("Whether the payment was reversed. A reversed payment stays in the list but "
        + "stops counting toward the installment's paid amount.")]
    bool IsReversed,
    [property: SwaggerSchema("Why it was reversed. Present only when IsReversed is true.")]
    [property: SwaggerExample("Pagamento em duplicidade.")]
    string? ReversalReason,
    [property: SwaggerSchema("Whether a proof of payment was uploaded. The file itself comes from the "
        + "download endpoint — only its presence is reported here.")]
    bool HasProof,
    [property: SwaggerSchema("When the payment was registered in the system, which can differ from "
        + "PaymentDate.")]
    DateTimeOffset CreatedAt,
    [property: SwaggerSchema("Who registered it.")]
    Guid CreatedById)
{
    public static PaymentResponse From(Payment p) => new(
        new PaymentId(p.Id), new InstallmentId(p.InstallmentId), p.PaymentDate, p.Amount, new BankAccountId(p.BankAccountId),
        p.Method, p.Interest, p.Fine, p.Discount, p.Notes, p.IsReversed, p.ReversalReason,
        p.ProofStoragePath != null, p.CreatedAt, p.CreatedById);
}
