namespace Ulbra.One.Common.Domain.Errors;

/// <summary>
/// The API's error vocabulary — the stable machine value that goes out as <c>error_code</c> and that the
/// frontend keys its pt-BR messages off (<c>web/src/i18n/locales/pt-BR.ts</c>, under <c>errors.codes</c>).
/// <para>
/// It lives here as constants rather than as literals at the call sites for one reason that pays for
/// itself: a catalogue that can be enumerated by reflection can be VERIFIED. `ErrorCatalogueTests` walks
/// these and fails the build when a code has no translation — the drift that left 114 of them showing
/// English to the user went unnoticed precisely because nothing could enumerate them.
/// </para>
/// <para>Adding an error means adding a constant here and its pt-BR message. The test enforces the pair.</para>
/// </summary>
public static class ErrorCodes
{
    public static class Accounting
    {
        public const string ConsolidatedForbidden = "accounting.consolidated_forbidden";
    }

    public static class AccountingPeriod
    {
        public const string Closed = "accounting_period.closed";
        public const string Invalid = "accounting_period.invalid";
        public const string InvalidTransition = "accounting_period.invalid_transition";
    }

    public static class Ai
    {
        public const string Disabled = "ai.disabled";
        public const string ProviderError = "ai.provider_error";
        public const string RateLimited = "ai.rate_limited";
        public const string Refused = "ai.refused";
        public const string SchemaMismatch = "ai.schema_mismatch";
    }

    public static class Assessment
    {
        public const string CompanyRequired = "assessment.company_required";
        public const string InvalidPeriod = "assessment.invalid_period";
    }

    public static class Attachment
    {
        public const string InvalidKind = "attachment.invalid_kind";
        public const string InvalidMetadata = "attachment.invalid_metadata";
    }

    public static class Auth
    {
        public const string AdminOnly = "auth.admin_only";
        public const string EmailNotVerified = "auth.email_not_verified";
        public const string GoogleNotConfigured = "auth.google_not_configured";
        public const string GoogleTokenInvalid = "auth.google_token_invalid";
        public const string UserInactive = "auth.user_inactive";
        public const string UserNotRegistered = "auth.user_not_registered";
    }

    public static class BankAccount
    {
        public const string Duplicate = "bank_account.duplicate";
        public const string InUse = "bank_account.in_use";
        public const string Invalid = "bank_account.invalid";
        public const string InvalidPayerCnpj = "bank_account.invalid_payer_cnpj";
        public const string LedgerAccountRequired = "bank_account.ledger_account_required";
        public const string NameRequired = "bank_account.name_required";
        public const string NotFound = "bank_account.not_found";
        public const string ReconciledAtInvalid = "bank_account.reconciled_at_invalid";
    }

    public static class BankTransaction
    {
        public const string AlreadyReconciled = "bank_transaction.already_reconciled";
        public const string NotFound = "bank_transaction.not_found";
        public const string NotReconciled = "bank_transaction.not_reconciled";
        public const string NotSuggested = "bank_transaction.not_suggested";
    }

    public static class BudgetAdjustment
    {
        public const string ApprovalLimitExceeded = "budget_adjustment.approval_limit_exceeded";
        public const string ApproverNotAuthorized = "budget_adjustment.approver_not_authorized";
        public const string CellNotFound = "budget_adjustment.cell_not_found";
        public const string CompanyMismatch = "budget_adjustment.company_mismatch";
        public const string CompetencyInvalid = "budget_adjustment.competency_invalid";
        public const string CostCenterInactive = "budget_adjustment.cost_center_inactive";
        public const string CostCenterNotFound = "budget_adjustment.cost_center_not_found";
        public const string CounterpartsRequired = "budget_adjustment.counterparts_required";
        public const string DimensionsInvalid = "budget_adjustment.dimensions_invalid";
        public const string Forbidden = "budget_adjustment.forbidden";
        public const string InvalidTransition = "budget_adjustment.invalid_transition";
        public const string JustificationRequired = "budget_adjustment.justification_required";
        public const string LimitWouldGoNegative = "budget_adjustment.limit_would_go_negative";
        public const string NotDeletable = "budget_adjustment.not_deletable";
        public const string NotEditable = "budget_adjustment.not_editable";
        public const string SourceEqualsDestination = "budget_adjustment.source_equals_destination";
        public const string SourceNotAllowed = "budget_adjustment.source_not_allowed";
        public const string SourceRequired = "budget_adjustment.source_required";
        public const string UnitNotFound = "budget_adjustment.unit_not_found";
        public const string UnitNotInCostCenter = "budget_adjustment.unit_not_in_cost_center";
        public const string ValueInvalid = "budget_adjustment.value_invalid";
    }

    public static class Cep
    {
        public const string Invalid = "cep.invalid";
    }

    public static class Cnpj
    {
        public const string LookupUnavailable = "cnpj.lookup_unavailable";
    }

    public static class Company
    {
        public const string CnpjDuplicate = "company.cnpj_duplicate";
        public const string CnpjInvalid = "company.cnpj_invalid";
        public const string LegalNameRequired = "company.legal_name_required";
        public const string NotFound = "company.not_found";
    }

    public static class Concurrency
    {
        public const string Conflict = "concurrency.conflict";
    }

    public static class CostCenter
    {
        public const string AutoApprovalLimitInvalid = "cost_center.auto_approval_limit_invalid";
        public const string CodeRequired = "cost_center.code_required";
        public const string NameRequired = "cost_center.name_required";
        public const string UnitsInvalid = "cost_center.units_invalid";
    }

    public static class CostCenterUnitBudget
    {
        public const string CompetencyHorizonExceeded = "cost_center_unit_budget.competency_horizon_exceeded";
        public const string CompetencyInvalid = "cost_center_unit_budget.competency_invalid";
        public const string CompetencyRangeInvalid = "cost_center_unit_budget.competency_range_invalid";
        public const string CostCenterInactive = "cost_center_unit_budget.cost_center_inactive";
        public const string DuplicateInPeriod = "cost_center_unit_budget.duplicate_in_period";
        public const string LimitInvalid = "cost_center_unit_budget.limit_invalid";
        public const string UnitInvalid = "cost_center_unit_budget.unit_invalid";
        public const string UnitNotInCostCenter = "cost_center_unit_budget.unit_not_in_cost_center";
    }

    public static class Customer
    {
        public const string NotFound = "customer.not_found";
    }

    public static class Document
    {
        public const string CpfInvalid = "document.cpf_invalid";
        public const string Invalid = "document.invalid";
    }

    public static class Employee
    {
        public const string NotFound = "employee.not_found";
    }

    public static class ExpenseCategory
    {
        public const string Duplicate = "expense_category.duplicate";
        public const string InUse = "expense_category.in_use";
        public const string NameRequired = "expense_category.name_required";
        public const string NotFound = "expense_category.not_found";
    }

    public static class File
    {
        public const string ContentMismatch = "file.content_mismatch";
        public const string ContentTypeMismatch = "file.content_type_mismatch";
        public const string Empty = "file.empty";
        public const string ExtensionNotAllowed = "file.extension_not_allowed";
        public const string TooLarge = "file.too_large";
        public const string UnsupportedType = "file.unsupported_type";
    }

    public static class FiscalDocument
    {
        public const string CompanyForbidden = "fiscal_document.company_forbidden";
        public const string EmptyExtraction = "fiscal_document.empty_extraction";
        public const string FileNameRequired = "fiscal_document.file_name_required";
        public const string InvalidMetadata = "fiscal_document.invalid_metadata";
        public const string NotFound = "fiscal_document.not_found";
        public const string ParseFailed = "fiscal_document.parse_failed";
        public const string TooLarge = "fiscal_document.too_large";
        public const string UnsupportedFormat = "fiscal_document.unsupported_format";
    }

    public static class General
    {
        public const string Null = "general.null";
        public const string Unexpected = "general.unexpected";
        public const string ValidationFailed = "general.validation_failed";
    }

    public static class GeneralLedger
    {
        public const string InvalidRange = "general_ledger.invalid_range";
    }

    public static class IncomeStatement
    {
        public const string InvalidRange = "income_statement.invalid_range";
    }

    public static class Installment
    {
        public const string NotFound = "installment.not_found";
        public const string NotOpen = "installment.not_open";
    }

    public static class JournalEntry
    {
        public const string AccountNotFound = "journal_entry.account_not_found";
        public const string CostCenterCompanyMismatch = "journal_entry.cost_center_company_mismatch";
        public const string CostCenterInactive = "journal_entry.cost_center_inactive";
        public const string CostCenterNotFound = "journal_entry.cost_center_not_found";
        public const string Invalid = "journal_entry.invalid";
        public const string NonPositiveAmount = "journal_entry.non_positive_amount";
        public const string NonPostableAccount = "journal_entry.non_postable_account";
        public const string NotDraft = "journal_entry.not_draft";
        public const string NotFound = "journal_entry.not_found";
        public const string NotPosted = "journal_entry.not_posted";
        public const string ReasonRequired = "journal_entry.reason_required";
        public const string TooFewLines = "journal_entry.too_few_lines";
        public const string Unbalanced = "journal_entry.unbalanced";
        public const string UnitCompanyMismatch = "journal_entry.unit_company_mismatch";
        public const string UnitInactive = "journal_entry.unit_inactive";
        public const string UnitNotFound = "journal_entry.unit_not_found";
        public const string Unreversible = "journal_entry.unreversible";
    }

    public static class LedgerAccount
    {
        public const string DuplicateCode = "ledger_account.duplicate_code";
        public const string Invalid = "ledger_account.invalid";
        public const string InvalidParent = "ledger_account.invalid_parent";
        public const string NotFound = "ledger_account.not_found";
        public const string NotPostable = "ledger_account.not_postable";
        public const string Required = "ledger_account.required";
    }

    public static class Match
    {
        public const string AmountMismatch = "match.amount_mismatch";
        public const string InstallmentInvalid = "match.installment_invalid";
    }

    public static class Payable
    {
        public const string BoletoDigitableLineInvalid = "payable.boleto_digitable_line_invalid";
        public const string CategoryInvalid = "payable.category_invalid";
        public const string CompanyForbidden = "payable.company_forbidden";
        public const string ConsolidatedForbidden = "payable.consolidated_forbidden";
        public const string CostCenterInvalid = "payable.cost_center_invalid";
        public const string FiscalDocumentMissing = "payable.fiscal_document_missing";
        public const string HasPayments = "payable.has_payments";
        public const string InstallmentNotFound = "payable.installment_not_found";
        public const string InstallmentsInvalid = "payable.installments_invalid";
        public const string InvalidState = "payable.invalid_state";
        public const string NoRequisitionOrigin = "payable.no_requisition_origin";
        public const string NotClassified = "payable.not_classified";
        public const string NotEditable = "payable.not_editable";
        public const string NotFound = "payable.not_found";
        public const string NotPayable = "payable.not_payable";
        public const string NotReclassifiable = "payable.not_reclassifiable";
        public const string OriginInvalid = "payable.origin_invalid";
        public const string ServiceNatureUndetermined = "payable.service_nature_undetermined";
        public const string UnitInvalid = "payable.unit_invalid";
        public const string WithholdingsInvalid = "payable.withholdings_invalid";
    }

    public static class Payment
    {
        public const string AlreadyReversed = "payment.already_reversed";
        public const string AmountInvalid = "payment.amount_invalid";
        public const string BankAccountInvalid = "payment.bank_account_invalid";
        public const string CompanyForbidden = "payment.company_forbidden";
        public const string ConsolidatedForbidden = "payment.consolidated_forbidden";
        public const string InstallmentInRemittance = "payment.installment_in_remittance";
        public const string InstallmentSettled = "payment.installment_settled";
        public const string NotFound = "payment.not_found";
        public const string Overpay = "payment.overpay";
        public const string PrincipalNegative = "payment.principal_negative";
    }

    public static class Presence
    {
        public const string InvalidConnectionId = "presence.invalid_connection_id";
        public const string UnknownScope = "presence.unknown_scope";
    }

    public static class Project
    {
        public const string BudgetLimitInvalid = "project.budget_limit_invalid";
        public const string Duplicate = "project.duplicate";
        public const string HasRequisitions = "project.has_requisitions";
        public const string InitialConsumedInvalid = "project.initial_consumed_invalid";
        public const string InvalidStatusTransition = "project.invalid_status_transition";
        public const string NameRequired = "project.name_required";
        public const string NotFound = "project.not_found";
        public const string UnitNotFound = "project.unit_not_found";
    }

    public static class RateLimit
    {
        public const string Exceeded = "rate_limit.exceeded";
    }

    public static class Receipt
    {
        public const string AdjustmentInvalid = "receipt.adjustment_invalid";
        public const string AlreadyReversed = "receipt.already_reversed";
        public const string AmountInvalid = "receipt.amount_invalid";
        public const string BankAccountInvalid = "receipt.bank_account_invalid";
        public const string DateBeforeIssue = "receipt.date_before_issue";
        public const string DateInFuture = "receipt.date_in_future";
        public const string DiscountInvalid = "receipt.discount_invalid";
        public const string InstallmentSettled = "receipt.installment_settled";
        public const string NotFound = "receipt.not_found";
        public const string PrincipalInvalid = "receipt.principal_invalid";
        public const string WouldExceedBalance = "receipt.would_exceed_balance";
    }

    public static class Receivable
    {
        public const string ConsolidatedForbidden = "receivable.consolidated_forbidden";
        public const string CostCenterInvalid = "receivable.cost_center_invalid";
        public const string CustomerInactive = "receivable.customer_inactive";
        public const string CustomerInvalid = "receivable.customer_invalid";
        public const string InstallmentNotFound = "receivable.installment_not_found";
        public const string InstallmentsInvalid = "receivable.installments_invalid";
        public const string InvalidState = "receivable.invalid_state";
        public const string NotEditable = "receivable.not_editable";
        public const string NotFound = "receivable.not_found";
        public const string NotReceivable = "receivable.not_receivable";
    }

    public static class Remittance
    {
        public const string BankAccountNotFound = "remittance.bank_account_not_found";
        public const string BankNotSupported = "remittance.bank_not_supported";
        public const string CompanyNotFound = "remittance.company_not_found";
        public const string InvalidPayerData = "remittance.invalid_payer_data";
        public const string MissingAgreementCode = "remittance.missing_agreement_code";
        public const string NotFound = "remittance.not_found";
        public const string NotGenerated = "remittance.not_generated";
        public const string NothingToRemit = "remittance.nothing_to_remit";
        public const string PaymentDateInPast = "remittance.payment_date_in_past";
        public const string SelectionTooLarge = "remittance.selection_too_large";
        public const string ReturnTrailerDivergent = "remittance.return_trailer_divergent";
    }

    public static class RemittanceLine
    {
        public const string InstallmentNotInRemittance = "remittance_line.installment_not_in_remittance";
        public const string NotFound = "remittance_line.not_found";
        public const string NotPending = "remittance_line.not_pending";
    }

    public static class Requisition
    {
        public const string AttachmentNotFound = "requisition.attachment_not_found";
        public const string BoletoDigitableLineInvalid = "requisition.boleto_digitable_line_invalid";
        public const string BudgetExceeded = "requisition.budget_exceeded";
        public const string CompanyMismatch = "requisition.company_mismatch";
        public const string CompetencyInvalid = "requisition.competency_invalid";
        public const string CompetencyRangeInvalid = "requisition.competency_range_invalid";
        public const string ConsolidatedForbidden = "requisition.consolidated_forbidden";
        public const string CostCenterInactive = "requisition.cost_center_inactive";
        public const string CostCenterNotFound = "requisition.cost_center_not_found";
        public const string CycleAfterEnd = "requisition.cycle_after_end";
        public const string CycleBeforeStart = "requisition.cycle_before_start";
        public const string CycleCompetencyInvalid = "requisition.cycle_competency_invalid";
        public const string CycleCompetencyTaken = "requisition.cycle_competency_taken";
        public const string CycleInvoiceRequired = "requisition.cycle_invoice_required";
        public const string DescriptionRequired = "requisition.description_required";
        public const string DimensionsInvalid = "requisition.dimensions_invalid";
        public const string EndBeforeLastCycle = "requisition.end_before_last_cycle";
        public const string EndBeforeStart = "requisition.end_before_start";
        public const string EstimateInvalid = "requisition.estimate_invalid";
        public const string FixedExpenseDuplicate = "requisition.fixed_expense_duplicate";
        public const string Forbidden = "requisition.forbidden";
        public const string HasPayments = "requisition.has_payments";
        public const string InstallmentAmountInvalid = "requisition.installment_amount_invalid";
        public const string InstallmentsInvalid = "requisition.installments_invalid";
        public const string InstallmentsRequired = "requisition.installments_required";
        public const string InvalidTransition = "requisition.invalid_transition";
        public const string IssueDateAfterFirstDueDate = "requisition.issue_date_after_first_due_date";
        public const string IssueDateRequired = "requisition.issue_date_required";
        public const string NotDeletable = "requisition.not_deletable";
        public const string NotEditable = "requisition.not_editable";
        public const string NotFound = "requisition.not_found";
        public const string NotInPayment = "requisition.not_in_payment";
        public const string NotRecurring = "requisition.not_recurring";
        public const string ProjectCompanyMismatch = "requisition.project_company_mismatch";
        public const string ProjectInactive = "requisition.project_inactive";
        public const string ProjectNotFound = "requisition.project_not_found";
        public const string RecurringPlanInvalid = "requisition.recurring_plan_invalid";
        public const string RecurringSingleInstallment = "requisition.recurring_single_installment";
        public const string ScopeForbidden = "requisition.scope_forbidden";
        public const string UnitNotFound = "requisition.unit_not_found";
        public const string UnitNotInCostCenter = "requisition.unit_not_in_cost_center";
    }

    public static class Roles
    {
        public const string InvalidPermission = "roles.invalid_permission";
    }

    public static class SlaReport
    {
        public const string InvalidRange = "sla_report.invalid_range";
    }

    public static class Statement
    {
        public const string ParseFailed = "statement.parse_failed";
    }

    public static class Storage
    {
        public const string PresignNotSupported = "storage.presign_not_supported";
    }

    public static class Transaction
    {
        public const string AmountInvalid = "transaction.amount_invalid";
        public const string CostCenterInvalid = "transaction.cost_center_invalid";
        public const string CustomerInvalid = "transaction.customer_invalid";
        public const string CustomerRequired = "transaction.customer_required";
        public const string InvalidState = "transaction.invalid_state";
        public const string UnitInvalid = "transaction.unit_invalid";
        public const string VendorRequired = "transaction.vendor_required";
    }

    public static class TrialBalance
    {
        public const string InvalidRange = "trial_balance.invalid_range";
    }

    public static class Unit
    {
        public const string AcronymRequired = "unit.acronym_required";
        public const string CnpjInvalid = "unit.cnpj_invalid";
        public const string NameRequired = "unit.name_required";
    }

    public static class User
    {
        public const string ApprovalLimitInvalid = "user.approval_limit_invalid";
        public const string EmailAlreadyRegistered = "user.email_already_registered";
        public const string EmailInvalid = "user.email_invalid";
        public const string RoleInvalid = "user.role_invalid";
        public const string ScopeCompanyMismatch = "user.scope_company_mismatch";
        public const string ScopeInvalid = "user.scope_invalid";
    }

    public static class Vendor
    {
        public const string DuplicateDocument = "vendor.duplicate_document";
        public const string NotFound = "vendor.not_found";
    }

    public static class WithholdingRule
    {
        public const string NotFound = "withholding_rule.not_found";
        public const string RateRequired = "withholding_rule.rate_required";
    }

    public static class YearEndClosing
    {
        public const string ApuracaoAccountMissing = "year_end_closing.apuracao_account_missing";
        public const string CompanyRequired = "year_end_closing.company_required";
        public const string InvalidYear = "year_end_closing.invalid_year";
        public const string RetainedEarningsAccountMissing = "year_end_closing.retained_earnings_account_missing";
    }
}
