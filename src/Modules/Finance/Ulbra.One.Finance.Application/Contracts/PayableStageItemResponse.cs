using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>
/// One row of a stage tab = one PAYABLE (not one installment). Used by the payable-grain stages
/// (Entry / Classification / Approval / Reconciled), where you act on the title, not the parcela.
/// <c>FirstDueDate</c> is the earliest installment due date; <c>TotalAmount</c> is the title total.
/// </summary>
[SwaggerSchema("One row of a payable-grain stage tab (Entry, Classification, Approval, Reconciled). The "
    + "grain here is the TITLE, not the installment — these stages act on the payable as a whole.")]
public sealed record PayableStageItemResponse(
    PayableId PayableId,
    /// <summary>Owning company — surfaced for the consolidated "Todas as empresas" list (AEL-906).</summary>
    [property: SwaggerSchema("Owning company, surfaced so the consolidated \"Todas as empresas\" list "
        + "can show which one each row belongs to.")]
    CompanyId CompanyId,
    [property: SwaggerSchema("Vendor name as recorded on the payable.")]
    [property: SwaggerExample("Construtora Horizonte Ltda")]
    string CounterpartyName,
    [property: SwaggerSchema("What the payable is for.")]
    [property: SwaggerExample("Manutenção preventiva do ar-condicionado — agosto")]
    string Description,
    [property: SwaggerSchema("Cost center the spend is charged to.")]
    CostCenterId CostCenterId,
    [property: SwaggerSchema("Expense category, when one was assigned.")]
    ExpenseCategoryId? CategoryId,
    [property: SwaggerSchema("Earliest due date across the payable's installments — what the list sorts "
        + "and ages by.")]
    [property: SwaggerExample("2026-08-10")]
    DateOnly FirstDueDate,
    [property: SwaggerSchema("How many installments the payable has.")]
    [property: SwaggerExample("3")]
    int InstallmentCount,
    [property: SwaggerSchema("Document total, in BRL, before withholdings.")]
    [property: SwaggerExample("4500.00")]
    decimal TotalAmount,
    /// <summary>Sum of the payable's withholdings (0 when there are none).</summary>
    [property: SwaggerSchema("Sum of the withholdings, in BRL. Zero when there are none.")]
    [property: SwaggerExample("209.25")]
    decimal TotalWithheld,
    /// <summary>TotalAmount − TotalWithheld — final once accounting completes (withholdings lock at Scheduled).</summary>
    [property: SwaggerSchema("What the vendor receives, in BRL: TotalAmount minus TotalWithheld. It is "
        + "only final once accounting completes, since the withholdings lock at Scheduled.")]
    [property: SwaggerExample("4290.75")]
    decimal NetTotal,
    [property: SwaggerSchema("Where the payable stands.")]
    PayableStatus Status,
    [property: SwaggerSchema("How the payable came to exist.")]
    PayableOrigin Origin,
    [property: SwaggerSchema("Whether a Nota Fiscal is registered.")]
    bool HasFiscalDocument,
    /// <summary>Applied ledger account (null while pending classification). Surfaced for the Contábil review tab.</summary>
    [property: SwaggerSchema("Ledger account actually applied. Null while the payable is still pending "
        + "classification.")]
    LedgerAccountId? AccountingAccountId = null,
    /// <summary>AI-suggested ledger account kept for human review (null once classified).</summary>
    [property: SwaggerSchema("Account the classifier suggested, kept for human review. Null once the "
        + "payable is classified — the applied account above is then the one that matters.")]
    LedgerAccountId? SuggestedAccountingAccountId = null,
    /// <summary>Confidence of the retained suggestion (null once classified).</summary>
    [property: SwaggerSchema("Confidence of the retained suggestion, 0 to 1. Null once classified.")]
    [property: SwaggerExample("0.92")]
    double? ClassificationConfidence = null,
    /// <summary>Rationale of the retained suggestion (null once classified).</summary>
    [property: SwaggerSchema("Why the classifier suggested that account. Null once classified.")]
    string? ClassificationRationale = null,
    /// <summary>Origin requisition's friendly code (#), for display/search on the Contábil list. Null unless requisition-sourced.</summary>
    [property: SwaggerSchema("Friendly code of the requisition that generated the payable — what the "
        + "accounting list displays and searches by. Null unless it came from a requisition.")]
    [property: SwaggerExample("482")]
    int? OriginRequisitionCode = null);
