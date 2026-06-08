using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Contracts;

public sealed record PayableResponse(
    PayableId Id, CompanyId CompanyId, PayableOrigin Origin, VendorId VendorId, string CounterpartyName,
    string? DocumentNumber, DateOnly? IssueDate, string Description, CostCenterId CostCenterId,
    ExpenseCategoryId? CategoryId, decimal TotalAmount, PayableStatus Status, string? RejectionReason,
    string? CancelReason, DateTimeOffset CreatedAt, IReadOnlyList<InstallmentResponse> Installments,
    IReadOnlyList<PayableAttachmentResponse> Attachments)
{
    public static PayableResponse From(Payable p) => new(
        new PayableId(p.Id), new CompanyId(p.CompanyId), p.Origin, new VendorId(p.VendorId), p.CounterpartyName,
        p.DocumentNumber, p.IssueDate, p.Description, new CostCenterId(p.CostCenterId),
        p.CategoryId is { } c ? new ExpenseCategoryId(c) : null, p.TotalAmount, p.Status, p.RejectionReason,
        p.CancelReason, p.CreatedAt,
        p.Installments.OrderBy(i => i.Sequence).Select(InstallmentResponse.From).ToList(),
        p.Attachments.OrderBy(a => a.CreatedAt).Select(PayableAttachmentResponse.From).ToList());
}
