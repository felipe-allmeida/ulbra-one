namespace Ulbra.One.FirestoreImport.Source;

/// <summary>A legacy `fornecedores` document (doc id = cnpjCpf, digits only). Flat address + bank fields;
/// the property names are the One-side terms, mapped from the Portuguese Firestore keys in FirestoreSource.</summary>
public sealed record VendorDoc(
    string Id,
    string? LegalName,
    string? Street,
    string? Number,
    string? Complement,
    string? Neighborhood,
    string? City,
    string? PostalCode,
    string? State,
    string? BankCode,
    string? Branch,
    string? Account,
    string? AccountDigit,
    string? PixKey,
    string? TaxRegime,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>A legacy `usuarios` document (doc id = email). Scope is embedded as string arrays; the
/// sentinel <c>"all"</c> may appear in <see cref="Units"/>/<see cref="Natures"/>.</summary>
public sealed record UserDoc(
    string Id,
    string? Email,
    string? Name,
    string? Role,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Units,
    IReadOnlyList<string> Natures,
    decimal? BudgetLimit,
    bool? HasApprovalAutonomy,
    bool? Active,
    string? Uid,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>A legacy `orcamentos` document (doc id = unitCode_natureName_competency). <c>LimitValue</c>
/// is sometimes an integer and sometimes a double in the source — read as decimal either way.</summary>
public sealed record BudgetDoc(
    string Id,
    string? UnitCode,
    string? UnitName,
    string? NatureName,
    string? Competency,
    decimal? LimitValue,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>A legacy `requisicoes` (requisition) document. Consumption is derived from these in the One
/// (BudgetControlService). <c>EstimateValue</c> is the TOTAL when <c>EstimateType='integral'</c> and the
/// PER-INSTALLMENT amount when <c>'mensal'</c> (paymentSystem Parcelado/Recorrente). The installment plan is
/// <c>PaymentInstallmentsCount</c> parcelas stepped by <c>PaymentFrequency</c> ('Mensal'=+1 month,
/// 'Quinzenal'=+15 days) from <c>FirstDueDate</c>. <c>PaymentSystem='Recorrente'</c> (often
/// EndCompetency=2100-xx) means open-ended. <c>AlreadyPaidInstallments</c> is present in the source but
/// effectively unused (≈4 docs), so realized-vs-committed is driven by <c>Status</c>, not by it.</summary>
public sealed record RequisitionDoc(
    string Id,
    string? UnitCode,
    string? NatureName,
    string? Status,
    string? Type,
    string? EstimateType,
    string? PaymentSystem,
    decimal? EstimateValue,
    string? StartCompetency,
    string? EndCompetency,
    string? ProviderCnpj,
    string? RequesterEmail,
    string? Description,
    long? SeqNum,
    string? FirstDueDate,
    long? PaymentInstallmentsCount,
    string? PaymentFrequency,
    string? Observation,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>A legacy `pagamentos` document — ONE per installment (id = <c>PAY_&lt;requisitionId&gt;_&lt;seq&gt;</c>).
/// This is the real Payable/baixa source: what was actually paid. <c>OriginalAmount</c> is the principal
/// (→ installment amount) and <c>Amount</c> is the cash paid (principal + <c>Interest</c>/<c>Fine</c>).
/// <c>IsExternal=true</c> marks the bulk `EXT_REQ_IMP_*` import buckets (out of Fase 1 scope).</summary>
public sealed record PaymentDoc(
    string Id,
    string? RequisitionId,
    long? Sequence,
    decimal? Amount,
    decimal? OriginalAmount,
    decimal? Interest,
    decimal? Fine,
    string? Competency,
    string? DueDate,
    string? PaymentDate,
    string? PaymentMethod,
    string? Status,
    string? ReturnStatus,
    string? ProviderCnpj,
    string? ProviderName,
    string? UnitCode,
    string? DigitableLine,
    string? PixKey,
    string? PixCopyAndPaste,
    string? RemittanceId,
    string? FiscalDocumentUrl,
    string? FiscalDocumentName,
    string? BoletoDocumentUrl,
    string? BoletoDocumentName,
    string? ReceiptUrl,
    string? ReceiptName,
    bool? IsExternal,
    string? ImportId,
    string? ImportType,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>A legacy `santander_config` document — a payer bank account (the source of `finance.bank_accounts`).</summary>
public sealed record BankAccountDoc(
    string Id,
    string? Branch,
    string? BranchDigit,
    string? Account,
    string? AccountDigit,
    string? Cnpj,
    string? LegalName,
    string? Agreement,
    string? Street,
    string? Number,
    string? Complement,
    string? City,
    string? PostalCode,
    string? State,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>A legacy `remessas` (CNAB remittance) document — links its `PaymentIds` to a payer bank account
/// (`BankAccountConfigId` → a `santander_config` id), used to resolve each payment's bank account.</summary>
public sealed record RemittanceDoc(
    string Id,
    string? BankAccountConfigId,
    IReadOnlyList<string> PaymentIds,
    long? Sequence,
    string? DateStr,
    string? Status,
    string? CreatedAt);

/// <summary>A legacy `adequacoes` document — a budget-ceiling adjustment. Imported as HISTORY only (it never
/// mutates the ceiling). A "Revisão Orçamentária" carries <see cref="CompetencyValues"/> (competency → value)
/// and fans out per month, its direction from <see cref="RevisionAction"/>; a "Transferência" carries a
/// <see cref="SourceCompetency"/>. <see cref="RequesterEmail"/> becomes created_by/updated_by (resolved to the
/// One user) and the transition actor; <see cref="CancelReason"/> is the legacy `cancelLog`.</summary>
public sealed record AdjustmentDoc(
    string Id,
    string? UnitCode,
    string? NatureName,
    string? Type,
    string? RevisionAction,
    string? Status,
    string? Competency,
    string? SourceCompetency,
    decimal? Value,
    IReadOnlyDictionary<string, decimal> CompetencyValues,
    string? Justification,
    string? Counterparts,
    string? RequesterEmail,
    long? SeqNum,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? CancelledAt,
    string? CancelReason);

/// <summary>A legacy `projetos` document — a project with its own spend ceiling. Metadata only (the One's
/// Project has no cost center and no attachments). <see cref="InitialConsumedValue"/> is spend incurred
/// before the system.</summary>
public sealed record ProjectDoc(
    string Id,
    string? Name,
    string? Description,
    decimal? BudgetLimit,
    decimal? InitialConsumedValue,
    string? UnitCode,
    string? Status,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>A legacy `requisicoes` doc's link to a project — used to backfill `requisitions.project_id`
/// without re-reading the whole requisition (the requisition importer ignores `projectId`).</summary>
public sealed record RequisitionProjectLink(string RequisitionId, string ProjectId);
