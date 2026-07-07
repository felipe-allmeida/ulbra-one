using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Contracts;

public sealed record PayableWithholdingResponse(
    PayableWithholdingId Id, WithholdingTaxType TaxType, decimal BaseAmount, decimal? Rate, decimal Amount)
{
    public static PayableWithholdingResponse From(PayableWithholding w) =>
        new(new PayableWithholdingId(w.Id), w.TaxType, w.BaseAmount, w.Rate, w.Amount);
}

public sealed record PayableResponse(
    PayableId Id, CompanyId CompanyId, PayableOrigin Origin, VendorId VendorId, string CounterpartyName,
    string? DocumentNumber, DateOnly? IssueDate, string Description, CostCenterId CostCenterId,
    ExpenseCategoryId? CategoryId, decimal TotalAmount, PayableStatus Status,
    string? RejectionReason, string? CancelReason,
    string? ApprovedBy, string? ApprovedByName, DateTimeOffset? ApprovedAt,
    string? RejectedBy, string? RejectedByName, DateTimeOffset? RejectedAt,
    ChartAccountId? AccountingAccountId,
    bool IsClassified, ChartAccountId? SuggestedAccountingAccountId,
    double? ClassificationConfidence, string? ClassificationRationale, PayableType? Type,
    BankAccountId? PaymentBankAccountId, PaymentMethod? PaymentMethod,
    DateTimeOffset CreatedAt, IReadOnlyList<InstallmentResponse> Installments,
    IReadOnlyList<PayableAttachmentResponse> Attachments,
    IReadOnlyList<PayableWithholdingResponse> Withholdings, decimal TotalWithheld, decimal NetTotal)
{
    /// <summary>Builds a <see cref="PayableResponse"/> from pre-computed installment responses
    /// (e.g. enriched with rail + remittance context by <see cref="Endpoints.Payables.GetPayable.GetPayable"/>).
    /// Approver/rejecter display names are resolved from <paramref name="actorNames"/> when provided.</summary>
    public static PayableResponse FromWithInstallments(
        Payable p, IReadOnlyList<InstallmentResponse> installments, IReadOnlyDictionary<Guid, string>? actorNames = null)
    {
        string? Name(string? id) =>
            id is not null && Guid.TryParse(id, out var g) && actorNames is not null && actorNames.TryGetValue(g, out var n)
                ? n
                : null;

        return new(
            new PayableId(p.Id), new CompanyId(p.CompanyId), p.Origin, new VendorId(p.VendorId), p.CounterpartyName,
            p.DocumentNumber, p.IssueDate, p.Description, new CostCenterId(p.CostCenterId),
            p.CategoryId is { } c ? new ExpenseCategoryId(c) : null, p.TotalAmount, p.Status, p.RejectionReason,
            p.CancelReason, p.ApprovedBy, Name(p.ApprovedBy), p.ApprovedAt, p.RejectedBy, Name(p.RejectedBy), p.RejectedAt,
            p.AccountingAccountId is { } a ? new ChartAccountId(a) : null,
            p.IsClassified,
            p.SuggestedAccountingAccountId is { } sa ? new ChartAccountId(sa) : null,
            p.ClassificationConfidence,
            p.ClassificationRationale,
            p.Type,
            p.PaymentBankAccountId is { } pb ? new BankAccountId(pb) : null,
            p.PaymentMethod,
            p.CreatedAt,
            installments,
            p.Attachments.OrderBy(a => a.CreatedAt).Select(PayableAttachmentResponse.From).ToList(),
            p.Withholdings.Select(PayableWithholdingResponse.From).ToList(),
            p.TotalWithheld,
            p.NetTotal);
    }

    public static PayableResponse From(Payable p, IReadOnlyDictionary<Guid, string>? actorNames = null) =>
        FromWithInstallments(p, p.Installments.OrderBy(i => i.Sequence).Select(InstallmentResponse.From).ToList(), actorNames);
}
