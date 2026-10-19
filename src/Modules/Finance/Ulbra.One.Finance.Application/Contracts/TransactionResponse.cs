using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>Response shape for a <see cref="Transaction"/>. Matches the frontend <c>Transaction</c> type.
/// <c>Type</c> and <c>Status</c> serialize as enum strings (e.g. "Payable", "Approved") via the global converter.</summary>
[SwaggerSchema("A simple transaction — the original single-entry model, superseded by Payable and "
    + "Receivable for new work. Still in use by the lançamentos screen, so it is not dead, but new "
    + "integrations should target the richer models instead.")]
public sealed record TransactionResponse(
    TransactionId Id,
    [property: SwaggerSchema("Company the transaction belongs to.")]
    CompanyId CompanyId,
    [property: SwaggerSchema("Unit it is charged to.")]
    UnitId UnitId,
    [property: SwaggerSchema("Cost center it is charged to.")]
    CostCenterId CostCenterId,
    [property: SwaggerSchema("Direction: money out (Payable) or money in (Receivable). It decides which "
        + "of VendorId and CustomerId is filled.")]
    TransactionType Type,
    [property: SwaggerSchema("What the transaction is for.")]
    [property: SwaggerExample("Reembolso de despesa de viagem")]
    string Description,
    [property: SwaggerSchema("Vendor, on a payable transaction. Null on a receivable one.")]
    VendorId? VendorId,
    [property: SwaggerSchema("Customer, on a receivable transaction. Null on a payable one.")]
    CustomerId? CustomerId,
    [property: SwaggerSchema("Counterparty name, denormalized so the row renders whichever side it is.")]
    [property: SwaggerExample("Construtora Horizonte Ltda")]
    string CounterpartyName,
    [property: SwaggerSchema("Amount, in BRL. Always positive — the direction is in Type.")]
    [property: SwaggerExample("1250.00")]
    decimal Amount,
    [property: SwaggerSchema("When it falls due.")]
    [property: SwaggerExample("2026-08-10")]
    DateOnly DueDate,
    [property: SwaggerSchema("Where the transaction stands.")]
    TransactionStatus Status,
    [property: SwaggerSchema("Why it was rejected. Present only on a rejected transaction.")]
    string? RejectionReason,
    [property: SwaggerSchema("When it was created.")]
    DateTime CreatedAt,
    [property: SwaggerSchema("When it was approved. Null while pending.")]
    DateTime? ApprovedAt,
    [property: SwaggerSchema("When it was settled. Null while unpaid.")]
    DateTime? PaidAt)
{
    public static TransactionResponse From(Transaction t) => new(
        new TransactionId(t.Id), new CompanyId(t.CompanyId), new UnitId(t.UnitId), new CostCenterId(t.CostCenterId),
        t.Type, t.Description,
        t.VendorId is { } v ? new VendorId(v) : null,
        t.CustomerId is { } c ? new CustomerId(c) : null,
        t.CounterpartyName,
        t.Amount, t.DueDate, t.Status, t.RejectionReason, t.CreatedAt, t.ApprovedAt, t.PaidAt);
}
