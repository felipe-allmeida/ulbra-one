using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Contracts;

[SwaggerSchema("One tax withheld from a payable. Withholdings reduce what the vendor receives, not what "
    + "the company owes — that difference is what separates TotalAmount from NetTotal.")]
public sealed record PayableWithholdingResponse(
    PayableWithholdingId Id,
    [property: SwaggerSchema("Which tax is withheld.")]
    WithholdingTaxType TaxType,
    [property: SwaggerSchema("Amount the rate was applied to, in BRL. Not always the payable total — the "
        + "rule decides whether the base is gross, net or the service portion.")]
    [property: SwaggerExample("4500.00")]
    decimal BaseAmount,
    [property: SwaggerSchema("Rate applied, as a percentage. Null when the amount came from a fixed "
        + "value rather than a rate.")]
    [property: SwaggerExample("4.65")]
    decimal? Rate,
    [property: SwaggerSchema("Amount withheld, in BRL.")]
    [property: SwaggerExample("209.25")]
    decimal Amount)
{
    public static PayableWithholdingResponse From(PayableWithholding w) =>
        new(new PayableWithholdingId(w.Id), w.TaxType, w.BaseAmount, w.Rate, w.Amount);
}

[SwaggerSchema("A payable (título a pagar) with its installments, withholdings and audit trail. The "
    + "money is owed at this level but paid at the installment level, so a payable is settled only once "
    + "every installment is.")]
public sealed record PayableResponse(
    PayableId Id,
    [property: SwaggerSchema("Company that owes.")]
    CompanyId CompanyId,
    [property: SwaggerSchema("How the payable came to exist: entered by hand, generated from an approved "
        + "requisition, or created by an integration.")]
    PayableOrigin Origin,
    [property: SwaggerSchema("Vendor to be paid.")]
    VendorId VendorId,
    [property: SwaggerSchema("Vendor name as recorded when the payable was created. A snapshot — renaming "
        + "the vendor later does not rewrite it.")]
    [property: SwaggerExample("Construtora Horizonte Ltda")]
    string CounterpartyName,
    [property: SwaggerSchema("Invoice number. Null until a Nota Fiscal is registered, and editable in any "
        + "state — including after payment.")]
    [property: SwaggerExample("00012345")]
    string? DocumentNumber,
    [property: SwaggerSchema("Emission date of the invoice. Null until one is registered.")]
    [property: SwaggerExample("2026-08-05")]
    DateOnly? IssueDate,
    [property: SwaggerSchema("What the payable is for.")]
    [property: SwaggerExample("Manutenção preventiva do ar-condicionado — agosto")]
    string Description,
    [property: SwaggerSchema("Cost center the spend is charged to.")]
    CostCenterId CostCenterId,
    [property: SwaggerSchema("Expense category, when one was assigned.")]
    ExpenseCategoryId? CategoryId,
    [property: SwaggerSchema("Document total, in BRL — the invoice's face value, before withholdings.")]
    [property: SwaggerExample("4500.00")]
    decimal TotalAmount,
    [property: SwaggerSchema("Where the payable stands.")]
    PayableStatus Status,
    [property: SwaggerSchema("Why it was rejected. Present only on a rejected payable.")]
    string? RejectionReason,
    [property: SwaggerSchema("Why it was cancelled. Present only on a cancelled payable.")]
    string? CancelReason,
    [property: SwaggerSchema("Id of whoever approved it.")]
    string? ApprovedBy,
    [property: SwaggerSchema("Display name of the approver, resolved for the screen. Null when it could "
        + "not be resolved — the id above stays the reliable field.")]
    [property: SwaggerExample("Maria Silva")]
    string? ApprovedByName,
    [property: SwaggerSchema("When it was approved. An auto-approved payable is stamped here too, so this "
        + "being set does not mean a human looked at it.")]
    DateTimeOffset? ApprovedAt,
    [property: SwaggerSchema("Id of whoever rejected it.")]
    string? RejectedBy,
    [property: SwaggerSchema("Display name of whoever rejected it.")]
    string? RejectedByName,
    [property: SwaggerSchema("When it was rejected.")]
    DateTimeOffset? RejectedAt,
    [property: SwaggerSchema("Id of the accountant who reviewed it.")]
    string? AccountedBy,
    [property: SwaggerSchema("Display name of the accountant who reviewed it.")]
    string? AccountedByName,
    [property: SwaggerSchema("When the accounting review was concluded.")]
    DateTimeOffset? AccountedAt,
    [property: SwaggerSchema("Ledger account the expense was booked to, once the accountant confirmed it.")]
    LedgerAccountId? AccountingAccountId,
    [property: SwaggerSchema("Whether the accounting classification is settled. An unclassified payable "
        + "cannot be paid.")]
    bool IsClassified,
    [property: SwaggerSchema("Account the system suggests. A suggestion only — it is not what was booked "
        + "until the accountant confirms it into AccountingAccountId.")]
    LedgerAccountId? SuggestedAccountingAccountId,
    [property: SwaggerSchema("How confident the classifier is in its suggestion, 0 to 1. Present only "
        + "when a suggestion exists.")]
    [property: SwaggerExample("0.92")]
    double? ClassificationConfidence,
    [property: SwaggerSchema("Why the classifier suggested that account, in its own words. Shown to the "
        + "accountant so the suggestion can be judged rather than trusted.")]
    string? ClassificationRationale,
    [property: SwaggerSchema("Nature of the spend — service or product. It must be determined before the "
        + "payable can be approved, because the withholdings depend on it.")]
    PayableType? Type,
    [property: SwaggerSchema("Bank account the payment goes out of.")]
    BankAccountId? PaymentBankAccountId,
    [property: SwaggerSchema("How the payable is to be paid. It constrains whether the installments can "
        + "travel in a CNAB remittance.")]
    PaymentMethod? PaymentMethod,
    [property: SwaggerSchema("When the payable was created.")]
    DateTimeOffset CreatedAt,
    [property: SwaggerSchema("The installments, in order. This is where the money actually moves.")]
    IReadOnlyList<InstallmentResponse> Installments,
    [property: SwaggerSchema("Files attached, oldest first.")]
    IReadOnlyList<PayableAttachmentResponse> Attachments,
    [property: SwaggerSchema("Taxes withheld from the vendor.")]
    IReadOnlyList<PayableWithholdingResponse> Withholdings,
    [property: SwaggerSchema("Sum of the withholdings, in BRL.")]
    [property: SwaggerExample("209.25")]
    decimal TotalWithheld,
    [property: SwaggerSchema("What the vendor actually receives, in BRL: TotalAmount minus TotalWithheld. "
        + "The withheld part is still owed — to the tax authority instead of the vendor.")]
    [property: SwaggerExample("4290.75")]
    decimal NetTotal,
    // AEL-793: the source requisition's id when this payable was generated from an approved requisition (else null).
    [property: SwaggerSchema("The requisition this payable came from. Null when it was entered directly, "
        + "which is what tells the two origins apart on screen.")]
    RequisitionId? OriginRequisitionId,
    // AEL-833: the unit (establishment) attributed to this payable, for accounting tracking (null when none).
    [property: SwaggerSchema("Unit (establishment) the payable is attributed to, for accounting tracking. "
        + "Null when none was attributed.")]
    UnitId? OriginUnitId)
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
            p.AccountedBy, Name(p.AccountedBy), p.AccountedAt,
            p.AccountingAccountId is { } a ? new LedgerAccountId(a) : null,
            p.IsClassified,
            p.SuggestedAccountingAccountId is { } sa ? new LedgerAccountId(sa) : null,
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
            p.NetTotal,
            p.OriginRequisitionId is { } orq ? new RequisitionId(orq) : null,
            p.OriginUnitId is { } ou ? new UnitId(ou) : null);
    }

    public static PayableResponse From(Payable p, IReadOnlyDictionary<Guid, string>? actorNames = null) =>
        FromWithInstallments(p, p.Installments.OrderBy(i => i.Sequence).Select(InstallmentResponse.From).ToList(), actorNames);
}
