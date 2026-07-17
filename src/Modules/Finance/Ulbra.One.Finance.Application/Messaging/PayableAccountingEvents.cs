namespace Ulbra.One.Finance.Application.Messaging;

/// <summary>A single tax withholding carried inside <see cref="PayableScheduled"/>.
/// Uses a plain string for TaxType so the Accounting module stays decoupled from Finance enums.</summary>
public sealed record ScheduledWithholding(string TaxType, decimal Amount);

/// <summary>Published when a payable transitions into the Scheduled status (approved + ready for payment).
/// <paramref name="AccountingAccountId"/> is the classified global <c>LedgerAccount</c> id (null when unclassified).
/// <paramref name="CompetenceDate"/> is the título's accounting competence (Payable.IssueDate; null for advances
/// with no NF — the incurral falls back to the posting date). <paramref name="CostCenterId"/> is propagated to
/// the expense line of the automatic incurral. <paramref name="DocumentNumber"/> is the payable's document/NF
/// number, carried onto the journal entry's human-facing SourceReference (null when the payable has none).</summary>
public sealed record PayableScheduled(
    Guid PayableId,
    Guid CompanyId,
    decimal Amount,
    Guid? AccountingAccountId,
    IReadOnlyList<ScheduledWithholding> Withholdings,
    DateOnly? CompetenceDate,
    Guid CostCenterId,
    string? DocumentNumber = null);

/// <summary>Published when a payment (baixa) is registered against a payable installment.
/// <paramref name="PaymentDate"/> is the settlement competence (Payment.PaymentDate).
/// <paramref name="CashAccountId"/> is the ledger account linked to the paying bank account (null when the
/// bank has no mapping — the settlement then falls back to the configured global cash code).
/// <paramref name="DocumentNumber"/> is the payable's document/NF number, carried onto the settlement entry's
/// human-facing SourceReference (null when the payable has none).</summary>
public sealed record PaymentRegistered(
    Guid PaymentId,
    Guid PayableId,
    Guid CompanyId,
    decimal Amount,
    DateOnly PaymentDate,
    Guid? CashAccountId,
    string? DocumentNumber = null);

/// <summary>Published when a payment is reversed (estornado).</summary>
public sealed record PaymentReversed(
    Guid PaymentId,
    Guid PayableId,
    Guid CompanyId);

/// <summary>Published when a payable is cancelled.</summary>
public sealed record PayableCancelled(
    Guid PayableId,
    Guid CompanyId);
