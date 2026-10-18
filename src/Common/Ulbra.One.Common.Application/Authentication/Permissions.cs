namespace Ulbra.One.Common.Application.Authentication;

/// <summary>
/// Central catalog of permissions — claims of type <see cref="ClaimType"/> in the
/// <c>resource:action</c> format. Roles compose subsets of these permissions and endpoints
/// require a permission via <c>.RequirePermission(...)</c>. It is the authorization vocabulary
/// shared across modules, so it lives in Common (referenced by constant — never
/// a cross-module type).
/// </summary>
public static class Permissions
{
    /// <summary>Claim type carried in the JWT for each granted permission.</summary>
    public const string ClaimType = "permission";

    // Base registries (Core): companies, units, cost centers.
    public const string CoreRead = "core:read";
    public const string CoreWrite = "core:write";

    // Third-party registries (Registry): vendors, customers, employees.
    public const string RegistryRead = "registry:read";
    public const string RegistryWrite = "registry:write";

    // Accounts payable (Finance/Payables).
    public const string PayablesRead = "payables:read";
    public const string PayablesWrite = "payables:write";
    public const string PayablesApprove = "payables:approve";
    public const string PayablesPay = "payables:pay";
    // Tax analysis: the Tax role validates/edits a payable's withholdings and releases it from the blocking
    // PendingTaxAnalysis stage (Serviço/Conta Fixa).
    public const string PayablesTax = "payables:tax";

    // Accounts receivable (Finance/Receivables).
    public const string ReceivablesRead = "receivables:read";
    public const string ReceivablesWrite = "receivables:write";
    public const string ReceivablesReceive = "receivables:receive";

    // Treasury: banks, reconciliation, remittances, cash flow, transactions.
    public const string TreasuryRead = "treasury:read";
    public const string TreasuryWrite = "treasury:write";
    public const string TreasuryApprove = "treasury:approve";

    // Accounting: journal entries, chart of accounts, postings.
    public const string AccountingRead = "accounting:read";
    public const string AccountingWrite = "accounting:write";
    public const string AccountingPost = "accounting:post";

    // Fiscal: assessment/EFD-Reinf, withholding rules.
    public const string FiscalRead = "fiscal:read";
    public const string FiscalWrite = "fiscal:write";

    // Purchase requisitions (Budgeting): creation and lifecycle of the requisitions.
    public const string RequisitionsRead = "requisitions:read";
    public const string RequisitionsWrite = "requisitions:write";
    // Approval workflow: triage (Controllership) and approval (Approver, no alçada — the budget
    // ceiling is the only block, resolved via Adequação, not via an override).
    public const string RequisitionsTriage = "requisitions:triage";
    public const string RequisitionsApprove = "requisitions:approve";
    // SLA analysis report (ADM + Approver).
    public const string RequisitionsSlaReport = "requisitions:sla-report";

    // Budget adequações (Budgeting): ceiling-adjustment workflow (supplementation/reduction/transfer).
    // Triage + approval with alçada, as in the requisitions. Provisional role↔permission mapping (AEL-771).
    public const string BudgetAdjustmentsRead = "budget-adjustments:read";
    public const string BudgetAdjustmentsWrite = "budget-adjustments:write";
    public const string BudgetAdjustmentsTriage = "budget-adjustments:triage";
    public const string BudgetAdjustmentsApprove = "budget-adjustments:approve";

    // Projects (Budgeting): accumulated spending ceilings and planned/actual tracking.
    public const string ProjectsRead = "projects:read";
    public const string ProjectsWrite = "projects:write";

    // Management reports (balance sheet, DRE, trial balance, aging, withholdings).
    public const string ReportsRead = "reports:read";

    // Administration.
    public const string AdminUsers = "admin:users";
    public const string AuditRead = "audit:read";

    /// <summary>All known permissions (used to validate/register policies).</summary>
    public static readonly IReadOnlyList<string> All =
    [
        CoreRead, CoreWrite,
        RegistryRead, RegistryWrite,
        PayablesRead, PayablesWrite, PayablesApprove, PayablesPay, PayablesTax,
        ReceivablesRead, ReceivablesWrite, ReceivablesReceive,
        TreasuryRead, TreasuryWrite, TreasuryApprove,
        AccountingRead, AccountingWrite, AccountingPost,
        FiscalRead, FiscalWrite,
        RequisitionsRead, RequisitionsWrite, RequisitionsTriage, RequisitionsApprove, RequisitionsSlaReport,
        BudgetAdjustmentsRead, BudgetAdjustmentsWrite, BudgetAdjustmentsTriage, BudgetAdjustmentsApprove,
        ProjectsRead, ProjectsWrite,
        ReportsRead,
        AdminUsers, AuditRead,
    ];
}
