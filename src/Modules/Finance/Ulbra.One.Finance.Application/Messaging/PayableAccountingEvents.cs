namespace Ulbra.One.Finance.Application.Messaging;

/// <summary>A single tax withholding carried inside <see cref="PayableScheduled"/>.
/// Uses a plain string for TaxType so the Accounting module stays decoupled from Finance enums.</summary>
public sealed record ScheduledWithholding(string TaxType, decimal Amount);

/// <summary>A single payment-time tax withholding (CSRF) carried inside <see cref="PaymentRegistered"/>,
/// already prorated to this baixa. Plain string TaxType — keeps Accounting decoupled from Finance enums.</summary>
public sealed record SettledWithholding(string TaxType, decimal Amount);

/// <summary>Published when a payable transitions into the Scheduled status (approved + ready for payment).
/// <paramref name="AccountingAccountId"/> is the classified global <c>LedgerAccount</c> id (null when unclassified).
/// <paramref name="CompetenceDate"/> is the título's accounting competence (Payable.IssueDate; null for advances
/// with no NF — the incurral falls back to the posting date). <paramref name="CostCenterId"/> is propagated to the
/// expense line; <paramref name="UnitId"/> (the estabelecimento, null when the payable has no unit) is propagated
/// to every line of the automatic incurral so the per-unit trial balance stays balanced. <paramref name="DocumentNumber"/>
/// is the payable's document/NF number, carried onto the journal entry's human-facing SourceReference (null when
/// the payable has none).</summary>
public sealed record PayableScheduled(
    Guid PayableId,
    Guid CompanyId,
    decimal Amount,
    Guid? AccountingAccountId,
    IReadOnlyList<ScheduledWithholding> Withholdings,
    DateOnly? CompetenceDate,
    Guid CostCenterId,
    string? DocumentNumber = null,
    string? CounterpartyName = null,
    string? CounterpartyDocument = null,
    Guid? UnitId = null);

/// <summary>Published when a payment (baixa) is registered against a payable installment.
/// <paramref name="PaymentDate"/> is the settlement competence (Payment.PaymentDate).
/// <paramref name="CashAccountId"/> is the ledger account linked to the paying bank account (null when the
/// bank has no mapping — the settlement then falls back to the configured global cash code).
/// <paramref name="Withholdings"/> are the CSRF (PIS/COFINS/CSLL) retentions recognized at this baixa, prorated
/// to the amount paid (empty when the payable has no payment-time retention). The settlement entry debits
/// Fornecedores a Pagar for <c>Amount + Σ Withholdings</c>, credits each retention account (taxes sharing an
/// account are summed into one line), and credits cash for <c>Amount</c>. <paramref name="UnitId"/> (the
/// estabelecimento) is set on every line of the settlement entry for per-unit tracking.
/// <paramref name="DocumentNumber"/> is the payable's document/NF number, carried onto the
/// settlement entry's human-facing SourceReference (null when the payable has none).
/// <paramref name="RequisitionId"/> and <paramref name="Competency"/> ("YYYY-MM" of the paid installment's due
/// date) are set only when the payable was generated from a Budgeting requisition — they let the Budgeting
/// consumer credit the real "realizado" back to that requisition/competency (null/ignored otherwise).
/// <paramref name="GrossAmount"/> is the gross-equivalent slice of the payable's TotalAmount realized by this
/// baixa: the payment's principal (interest/fine/discount excluded) grossed up by TotalAmount/NetTotal, with a
/// running-sum so the settling payment lands exactly on TotalAmount. It exists so the requisition's realizado
/// (compared against the GROSS estimate) closes exactly even when the payable is settled net of withholdings.
/// Null on messages published before the field existed; the Budgeting consumer falls back to <c>Amount</c>.</summary>
public sealed record PaymentRegistered(
    Guid PaymentId,
    Guid PayableId,
    Guid CompanyId,
    decimal Amount,
    DateOnly PaymentDate,
    Guid? CashAccountId,
    IReadOnlyList<SettledWithholding> Withholdings,
    string? DocumentNumber = null,
    string? CounterpartyName = null,
    string? CounterpartyDocument = null,
    Guid? RequisitionId = null,
    string? Competency = null,
    decimal? GrossAmount = null,
    Guid? UnitId = null);

/// <summary>Published when a payment is reversed (estornado). <paramref name="RequisitionId"/> is set only
/// when the payable was requisition-sourced, so the Budgeting consumer can back out the "realizado".</summary>
public sealed record PaymentReversed(
    Guid PaymentId,
    Guid PayableId,
    Guid CompanyId,
    Guid? RequisitionId = null);

/// <summary>Published when a payable is cancelled.</summary>
public sealed record PayableCancelled(
    Guid PayableId,
    Guid CompanyId);

/// <summary>Published when a requisition-sourced payable is rejected, so Budgeting returns the origin requisition
/// to the chosen role. <paramref name="ReturnTarget"/> is a neutral string ("Requester"/"Controllership") to keep
/// Budgeting decoupled from Finance enums (AEL-935).</summary>
public sealed record PayableRejected(
    Guid PayableId,
    Guid CompanyId,
    Guid RequisitionId,
    string? Reason,
    string RejectedBy,
    string ReturnTarget);

/// <summary>Published when a payable's document/NF number is edited AFTER accounting entries were already posted
/// (e.g. Contábil só recebe a NF depois do pagamento). Carries the NEW <paramref name="DocumentNumber"/> and the
/// payable's <paramref name="PaymentIds"/>, so Accounting can refresh the human-facing SourceReference (número da
/// NF exibido no Livro Razão) of the already-posted incurral AND settlement entries — a metadata-only amend, no
/// rebook/reversal. Settlement entries are keyed by payment id, so they can only be found via these ids.</summary>
public sealed record PayableDocumentNumberChanged(
    Guid PayableId,
    Guid CompanyId,
    string? DocumentNumber,
    IReadOnlyList<Guid> PaymentIds);

/// <summary>Published when Contábil reclassifies a scheduled/paid payable's expense account (post-fato); carries the
/// incurral payload with the NEW account so Accounting rebooks it. Only the expense debit moves.</summary>
public sealed record PayableReclassified(
    Guid PayableId,
    Guid CompanyId,
    decimal Amount,
    Guid? AccountingAccountId,
    IReadOnlyList<ScheduledWithholding> Withholdings,
    DateOnly? CompetenceDate,
    Guid CostCenterId,
    string? DocumentNumber = null,
    string? CounterpartyName = null,
    string? CounterpartyDocument = null,
    Guid? UnitId = null);
