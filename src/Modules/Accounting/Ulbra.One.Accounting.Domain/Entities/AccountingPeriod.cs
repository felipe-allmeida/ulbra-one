using Ulbra.One.Common.Domain.Auditing;

namespace Ulbra.One.Accounting.Domain.Entities;

/// <summary>An accounting period (competência mensal) for a company: the <c>(FiscalYear, Month)</c> bucket
/// whose lifecycle gates whether entries may be booked into that competence. <c>Open</c> accepts postings,
/// <c>Closed</c> blocks them (the closed-period guard), <c>Closing</c> is the transitional state used by the
/// monthly-close routine (Module 3). Keyed uniquely per <see cref="CompanyId"/> + year/month.</summary>
public sealed class AccountingPeriod : IAuditable
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CompanyId { get; private set; }
    public int FiscalYear { get; private set; }
    public int Month { get; private set; }
    public AccountingPeriodStatus Status { get; private set; } = AccountingPeriodStatus.Open;

    // IAuditable
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    private AccountingPeriod() { }

    public AccountingPeriod(Guid companyId, int fiscalYear, int month)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId required", nameof(companyId));
        if (fiscalYear < 1) throw new ArgumentOutOfRangeException(nameof(fiscalYear), "FiscalYear must be positive.");
        if (month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month), "Month must be between 1 and 12.");
        CompanyId = companyId;
        FiscalYear = fiscalYear;
        Month = month;
    }

    public bool IsClosed => Status == AccountingPeriodStatus.Closed;

    /// <summary>Begins the monthly close (Open → Closing). Idempotent when already closing; reserved for the
    /// close routine (Module 3). A closed period must be reopened first.</summary>
    public void StartClosing()
    {
        if (Status == AccountingPeriodStatus.Closing) return;
        if (Status != AccountingPeriodStatus.Open)
            throw new AccountingPeriodException(AccountingPeriodErrors.InvalidTransition);
        Status = AccountingPeriodStatus.Closing;
    }

    /// <summary>Closes the period so no entry may be booked into its competence. Idempotent when already closed.</summary>
    public void Close() => Status = AccountingPeriodStatus.Closed;

    /// <summary>Reopens the period for postings. Idempotent when already open.</summary>
    public void Reopen() => Status = AccountingPeriodStatus.Open;

    public void MarkCreated(string userId, DateTimeOffset at) { CreatedBy = userId; CreatedAt = at; MarkUpdated(userId, at); }
    public void MarkUpdated(string userId, DateTimeOffset at) { UpdatedBy = userId; UpdatedAt = at; }
}
