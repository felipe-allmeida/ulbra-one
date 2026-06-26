using Ulbra.One.Common.Domain.Auditing;
using Ulbra.One.Finance.Domain.Remittances;

namespace Ulbra.One.Finance.Domain.Entities;

/// <summary>Aggregate root for a CNAB240 payment remittance batch.</summary>
public class Remittance : IAuditable, ISoftDeletable
{
    private readonly List<RemittanceLine> _lines = new();

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CompanyId { get; private set; }
    public Guid PayingBankAccountId { get; private set; }
    public string BankCode { get; private set; } = string.Empty;
    public int SequenceNumber { get; private set; }
    public RemittanceStatus Status { get; private set; } = RemittanceStatus.Generated;
    public string? FileStorageKey { get; private set; }
    public string? FileName { get; private set; }
    public long? FileSizeBytes { get; private set; }
    public DateTimeOffset GeneratedAt { get; private set; }
    public string GeneratedBy { get; private set; } = string.Empty;

    public IReadOnlyList<RemittanceLine> Lines => _lines;

    public decimal TotalAmount => _lines.Sum(l => l.Amount);

    public DateTimeOffset? DeletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    private Remittance() { }

    public static Remittance Create(Guid companyId, Guid payingBankAccountId, string bankCode,
        int sequenceNumber, string generatedBy, DateTimeOffset at)
    {
        return new Remittance
        {
            CompanyId = companyId,
            PayingBankAccountId = payingBankAccountId,
            BankCode = bankCode,
            SequenceNumber = sequenceNumber,
            GeneratedBy = generatedBy,
            GeneratedAt = at,
            Status = RemittanceStatus.Generated,
        };
    }

    /// <summary>Appends a new line to the remittance. Audit timestamps are applied by the AuditInterceptor on save.</summary>
    public RemittanceLine AddLine(RemittanceLineDraft draft)
    {
        var sequence = _lines.Count + 1;
        var line = new RemittanceLine(Id, draft, sequence, SequenceNumber);
        _lines.Add(line);
        return line;
    }

    public void SetFile(string storageKey, string fileName, long sizeBytes)
    {
        FileStorageKey = storageKey;
        FileName = fileName;
        FileSizeBytes = sizeBytes;
    }

    public void Cancel()
    {
        if (Status != RemittanceStatus.Generated)
            throw new InvalidOperationException("Only a Generated remittance can be cancelled.");
        Status = RemittanceStatus.Cancelled;
    }

    /// <summary>
    /// Recomputes the remittance status based on its lines. Has no effect on a <see cref="RemittanceStatus.Cancelled"/> remittance.
    /// <list type="bullet">
    ///   <item>All lines <see cref="RemittanceLineStatus.Paid"/>, none <see cref="RemittanceLineStatus.Returned"/> → <see cref="RemittanceStatus.Confirmed"/>.</item>
    ///   <item>Some lines <see cref="RemittanceLineStatus.Returned"/>, rest resolved (no <see cref="RemittanceLineStatus.Pending"/>) → <see cref="RemittanceStatus.PartiallyReturned"/>.</item>
    ///   <item>Any line still <see cref="RemittanceLineStatus.Pending"/> → stays <see cref="RemittanceStatus.Generated"/>.</item>
    /// </list>
    /// </summary>
    public void Recompute()
    {
        if (Status == RemittanceStatus.Cancelled)
            return;

        bool anyPending = _lines.Any(l => l.Status == RemittanceLineStatus.Pending);
        if (anyPending)
        {
            Status = RemittanceStatus.Generated;
            return;
        }

        bool anyReturned = _lines.Any(l => l.Status == RemittanceLineStatus.Returned);
        Status = anyReturned ? RemittanceStatus.PartiallyReturned : RemittanceStatus.Confirmed;
    }

    public void MarkDeleted(DateTimeOffset at) => DeletedAt = at;
    public void MarkCreated(string userId, DateTimeOffset at) { CreatedBy = userId; CreatedAt = at; MarkUpdated(userId, at); }
    public void MarkUpdated(string userId, DateTimeOffset at) { UpdatedBy = userId; UpdatedAt = at; }
}
