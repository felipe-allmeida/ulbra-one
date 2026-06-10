using Ulbra.One.Common.Domain.Auditing;

namespace Ulbra.One.Finance.Domain.Entities;

/// <summary>One bank statement import (an OFX/CSV upload) for a bank account.</summary>
public class BankStatementImport : IAuditable
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CompanyId { get; private set; }
    public Guid BankAccountId { get; private set; }
    public StatementSource Source { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public DateOnly? PeriodStart { get; private set; }
    public DateOnly? PeriodEnd { get; private set; }
    public int TransactionCount { get; private set; }
    public int NewCount { get; private set; }
    public int DuplicateCount { get; private set; }
    public ImportStatus Status { get; private set; } = ImportStatus.Completed;

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    private BankStatementImport() { }

    public BankStatementImport(Guid companyId, Guid bankAccountId, StatementSource source, string fileName,
        DateOnly? periodStart, DateOnly? periodEnd, int transactionCount, int newCount, int duplicateCount)
    {
        CompanyId = companyId;
        BankAccountId = bankAccountId;
        Source = source;
        FileName = fileName;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        TransactionCount = transactionCount;
        NewCount = newCount;
        DuplicateCount = duplicateCount;
    }

    public void MarkCreated(string userId, DateTimeOffset at) { CreatedBy = userId; CreatedAt = at; MarkUpdated(userId, at); }
    public void MarkUpdated(string userId, DateTimeOffset at) { UpdatedBy = userId; UpdatedAt = at; }
}
