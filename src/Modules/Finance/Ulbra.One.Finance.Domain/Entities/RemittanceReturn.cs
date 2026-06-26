using Ulbra.One.Common.Domain.Auditing;

namespace Ulbra.One.Finance.Domain.Entities;

/// <summary>
/// History record for a processed CNAB240 return file. Tracks aggregate counts for matching/payment/return outcomes.
/// Not a child of <see cref="Remittance"/> — standalone aggregate keyed by <see cref="Id"/>.
/// </summary>
public class RemittanceReturn : IAuditable, ISoftDeletable
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CompanyId { get; private set; }
    public string FileStorageKey { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;

    /// <summary>Total lines in the return file that were matched to a remittance line.</summary>
    public int MatchedCount { get; private set; }

    /// <summary>Lines resolved as paid.</summary>
    public int PaidCount { get; private set; }

    /// <summary>Lines resolved as returned (rejected by bank).</summary>
    public int ReturnedCount { get; private set; }

    /// <summary>Lines in the return file that could not be matched to any known remittance line.</summary>
    public int UnmatchedCount { get; private set; }

    /// <summary>Lines where the return amount diverged from the remittance amount.</summary>
    public int DivergentCount { get; private set; }

    public DateTimeOffset ProcessedAt { get; private set; }
    public string ProcessedBy { get; private set; } = string.Empty;

    public DateTimeOffset? DeletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    private RemittanceReturn() { }

    public static RemittanceReturn Create(
        Guid companyId,
        string fileStorageKey,
        string fileName,
        int matchedCount,
        int paidCount,
        int returnedCount,
        int unmatchedCount,
        int divergentCount,
        string processedBy,
        DateTimeOffset processedAt)
    {
        return new RemittanceReturn
        {
            CompanyId = companyId,
            FileStorageKey = fileStorageKey,
            FileName = fileName,
            MatchedCount = matchedCount,
            PaidCount = paidCount,
            ReturnedCount = returnedCount,
            UnmatchedCount = unmatchedCount,
            DivergentCount = divergentCount,
            ProcessedBy = processedBy,
            ProcessedAt = processedAt,
        };
    }

    public void MarkDeleted(DateTimeOffset at) => DeletedAt = at;
    public void MarkCreated(string userId, DateTimeOffset at) { CreatedBy = userId; CreatedAt = at; MarkUpdated(userId, at); }
    public void MarkUpdated(string userId, DateTimeOffset at) { UpdatedBy = userId; UpdatedAt = at; }
}
