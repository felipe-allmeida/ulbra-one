using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>Response shape for a <see cref="Transaction"/>. Matches the frontend <c>Transaction</c> type.
/// <c>Type</c> and <c>Status</c> serialize as enum strings (e.g. "Payable", "Approved") via the global converter.</summary>
public sealed record TransactionResponse(
    TransactionId Id,
    CompanyId CompanyId,
    UnitId UnitId,
    CostCenterId CostCenterId,
    TransactionType Type,
    string Description,
    VendorId? VendorId,
    CustomerId? CustomerId,
    string CounterpartyName,
    decimal Amount,
    DateOnly DueDate,
    TransactionStatus Status,
    string? RejectionReason,
    DateTime CreatedAt,
    DateTime? ApprovedAt,
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
