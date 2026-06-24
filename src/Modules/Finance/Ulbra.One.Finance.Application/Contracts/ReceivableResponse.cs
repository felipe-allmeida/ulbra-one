using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Contracts;

public sealed record ReceivableResponse(
    ReceivableId Id, CompanyId CompanyId, ReceivableOrigin Origin, CustomerId PayerId, string PayerName,
    string? DocumentNumber, DateOnly? IssueDate, string Description, CostCenterId CostCenterId,
    Guid? RevenueCategoryId, Guid? LedgerAccountId, decimal TotalAmount, ReceivableStatus Status,
    string? CancelReason, string? WriteOffReason, DateTimeOffset CreatedAt,
    IReadOnlyList<ReceivableInstallmentResponse> Installments)
{
    public static ReceivableResponse From(Receivable r, DateOnly today) => new(
        new ReceivableId(r.Id), new CompanyId(r.CompanyId), r.Origin, new CustomerId(r.PayerId), r.PayerName,
        r.DocumentNumber, r.IssueDate, r.Description, new CostCenterId(r.CostCenterId),
        r.RevenueCategoryId, r.LedgerAccountId, r.TotalAmount, r.Status, r.CancelReason, r.WriteOffReason, r.CreatedAt,
        r.Installments.OrderBy(i => i.Sequence).Select(i => ReceivableInstallmentResponse.From(i, today, r.IsReceivable)).ToList());
}
