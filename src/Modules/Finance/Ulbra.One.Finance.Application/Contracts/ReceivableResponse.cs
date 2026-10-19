using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Contracts;

[SwaggerSchema("A receivable (título a receber) with its installments — the mirror of a payable, on the "
    + "money-in side.")]
public sealed record ReceivableResponse(
    ReceivableId Id,
    [property: SwaggerSchema("Company that is owed.")]
    CompanyId CompanyId,
    [property: SwaggerSchema("How the receivable came to exist.")]
    ReceivableOrigin Origin,
    [property: SwaggerSchema("Customer who owes.")]
    CustomerId PayerId,
    [property: SwaggerSchema("Customer name as recorded when the receivable was created — a snapshot.")]
    [property: SwaggerExample("Prefeitura Municipal de Canoas")]
    string PayerName,
    [property: SwaggerSchema("Invoice number, when one was issued.")]
    [property: SwaggerExample("00012345")]
    string? DocumentNumber,
    [property: SwaggerSchema("Emission date of the invoice.")]
    [property: SwaggerExample("2026-08-05")]
    DateOnly? IssueDate,
    [property: SwaggerSchema("What the receivable is for.")]
    [property: SwaggerExample("Convênio de estágio — agosto")]
    string Description,
    [property: SwaggerSchema("Cost center the revenue is attributed to.")]
    CostCenterId CostCenterId,
    [property: SwaggerSchema("Revenue category, when one was assigned.")]
    Guid? RevenueCategoryId,
    [property: SwaggerSchema("Ledger account the revenue posts to.")]
    Guid? LedgerAccountId,
    [property: SwaggerSchema("Total owed, in BRL.")]
    [property: SwaggerExample("18000.00")]
    decimal TotalAmount,
    [property: SwaggerSchema("Where the receivable stands.")]
    ReceivableStatus Status,
    [property: SwaggerSchema("Why it was cancelled. Present only on a cancelled receivable.")]
    string? CancelReason,
    [property: SwaggerSchema("Why it was written off. Present only on a written-off receivable — that is "
        + "giving up on collecting, which is not the same as cancelling.")]
    string? WriteOffReason,
    [property: SwaggerSchema("When the receivable was created.")]
    DateTimeOffset CreatedAt,
    [property: SwaggerSchema("The installments, in order.")]
    IReadOnlyList<ReceivableInstallmentResponse> Installments)
{
    public static ReceivableResponse From(Receivable r, DateOnly today) => new(
        new ReceivableId(r.Id), new CompanyId(r.CompanyId), r.Origin, new CustomerId(r.PayerId), r.PayerName,
        r.DocumentNumber, r.IssueDate, r.Description, new CostCenterId(r.CostCenterId),
        r.RevenueCategoryId, r.LedgerAccountId, r.TotalAmount, r.Status, r.CancelReason, r.WriteOffReason, r.CreatedAt,
        r.Installments.OrderBy(i => i.Sequence).Select(i => ReceivableInstallmentResponse.From(i, today, r.IsReceivable)).ToList());
}
