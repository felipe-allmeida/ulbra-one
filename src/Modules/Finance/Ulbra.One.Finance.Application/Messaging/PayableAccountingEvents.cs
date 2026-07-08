namespace Ulbra.One.Finance.Application.Messaging;

/// <summary>A single tax withholding carried inside <see cref="PayableScheduled"/>.
/// Uses a plain string for TaxType so the Accounting module stays decoupled from Finance enums.</summary>
public sealed record ScheduledWithholding(string TaxType, decimal Amount);

/// <summary>Published when a payable transitions into the Scheduled status (approved + ready for payment).
/// <paramref name="AccountingAccountId"/> is the classified global <c>LedgerAccount</c> id (null when unclassified).</summary>
public sealed record PayableScheduled(
    Guid PayableId,
    Guid CompanyId,
    decimal Amount,
    Guid? AccountingAccountId,
    IReadOnlyList<ScheduledWithholding> Withholdings);

/// <summary>Published when a payment (baixa) is registered against a payable installment.</summary>
public sealed record PaymentRegistered(
    Guid PaymentId,
    Guid PayableId,
    Guid CompanyId,
    decimal Amount);

/// <summary>Published when a payment is reversed (estornado).</summary>
public sealed record PaymentReversed(
    Guid PaymentId,
    Guid PayableId,
    Guid CompanyId);

/// <summary>Published when a payable is cancelled.</summary>
public sealed record PayableCancelled(
    Guid PayableId,
    Guid CompanyId);
