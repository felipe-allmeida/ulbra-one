namespace Ulbra.One.Budgeting.Domain;

/// <summary>Kind of purchase requisition — mirrors the legacy app's requisition types.</summary>
public enum RequisitionType
{
    Project,
    ServiceProvision,
    Purchase,
    FixedExpense,
}

/// <summary>How <see cref="Entities.Requisition.EstimateValue"/> maps onto the competency window:
/// <see cref="Monthly"/> repeats per month across the window; <see cref="Full"/> lands entirely on the
/// start competency.</summary>
public enum EstimateType
{
    Monthly,
    Full,
}

/// <summary>Cadence of the payment plan. Affects total cost (not the competency window):
/// <see cref="Biweekly"/> bills twice a month, so total = value × count / 2.</summary>
public enum PaymentFrequency
{
    Biweekly,
    Monthly,
    Semiannual,
    Annual,
}

/// <summary>Lifecycle state of a project. <see cref="Active"/> projects accrue requisitions against the
/// ceiling; <see cref="Finished"/> and <see cref="Cancelled"/> are terminal.</summary>
public enum ProjectStatus
{
    Active,
    Finished,
    Cancelled,
}

/// <summary>Lifecycle state of a requisition. Transitions are guarded in the domain and driven by the esteira
/// endpoints (triagem, complemento financeiro, aprovação) and the Finance payment integration.</summary>
public enum RequisitionStatus
{
    Draft,
    Triage,
    Approval,
    Complement,
    FinancialComplement,
    InPayment,
    AwaitingFiscalDocument,
    Paid,
    Finalized,
    Cancelled,
}

/// <summary>The action behind a status transition, recorded on each <see cref="Entities.RequisitionTransition"/>.</summary>
public enum RequisitionAction
{
    Submit,
    ApproveTriage,
    SubmitFinancialComplement,
    ReturnToTriageForRevalue,
    ReturnForComplement,
    ReturnToFinancialComplement,
    Resubmit,
    Approve,
    Cancel,
    MarkAwaitingFiscalDocument,
    MarkPaid,
    MarkFinalized,
    ReopenForPayment,
    ReturnFromPayment,
    AddRecurrenceCycle,
    TerminateRecurrence,
    RemoveRecurrenceCycle,
}

/// <summary>Role a rejected requisition-sourced payable is returned to: <see cref="Requester"/> (→ Complement,
/// to edit and resubmit) or <see cref="Controllership"/> (→ Triage, to re-triage) — AEL-935.</summary>
public enum RequisitionReturnTarget
{
    Requester,
    Controllership,
}

/// <summary>What a requisition attachment represents, so the Solicitante declares each file (mirrors the legacy
/// app's per-type fields). <see cref="FiscalDocument"/> marks it as a Nota Fiscal so that, when the requisition
/// is approved, Finance promotes the file to the Payable's fiscal document (feeding withholdings + the AI
/// classification) and it is the only kind that clears the "NF pendente" gate. <see cref="BankSlip"/> (boleto)
/// and <see cref="Invoice"/> (fatura) are payment/billing documents; <see cref="Other"/> is any supporting file
/// (quote, contract). Persisted as a string; default <see cref="Other"/>.</summary>
public enum RequisitionAttachmentKind
{
    Other,
    FiscalDocument,
    BankSlip,
    Invoice,
}

/// <summary>Kind of budget adjustment (adequação orçamentária) — mirrors the legacy app's three types.
/// <see cref="Supplementation"/> raises a cell's ceiling; <see cref="Reduction"/> lowers it;
/// <see cref="Transfer"/> moves value between two competencies of the same Unit × CostCenter
/// (source −, destination +).</summary>
public enum BudgetAdjustmentType
{
    Supplementation,
    Reduction,
    Transfer,
}

/// <summary>Lifecycle state of a budget adjustment. Two-step chain (mirrors the legacy Administrador →
/// Presidência): <see cref="AwaitingTriage"/> → <see cref="AwaitingApproval"/> → <see cref="Approved"/>
/// (moves the budget), plus <see cref="Reversed"/> (recomposes it) and <see cref="Cancelled"/>.</summary>
public enum BudgetAdjustmentStatus
{
    AwaitingTriage,
    AwaitingApproval,
    Approved,
    Reversed,
    Cancelled,
}

/// <summary>The action behind an adequação status transition, recorded on each
/// <see cref="Entities.BudgetAdjustmentTransition"/>.</summary>
public enum BudgetAdjustmentAction
{
    SendToApproval,
    Approve,
    Cancel,
    Reverse,
}
