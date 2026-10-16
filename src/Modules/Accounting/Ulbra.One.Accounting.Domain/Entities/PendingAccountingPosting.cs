namespace Ulbra.One.Accounting.Domain.Entities;

/// <summary>A payable accounting posting the system could not auto-resolve (a ledger code was missing).
/// Surfaced for an accountant to post manually or fix the chart. Resolved once a real entry exists.</summary>
public sealed class PendingAccountingPosting
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CompanyId { get; private set; }
    public string SourceKey { get; private set; } = string.Empty;
    public PostingKind Kind { get; private set; }
    public decimal Amount { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string IntendedCodes { get; private set; } = string.Empty;

    /// <summary>Opaque serialized replay plan (competence + line codes/sides/amounts/cost centers) captured
    /// when parking, so the posting can be reprocessed after the mapping is fixed / period reopened. Null on
    /// legacy rows parked before replay support.</summary>
    public string? Plan { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    private PendingAccountingPosting() { }

    public PendingAccountingPosting(Guid companyId, string sourceKey, PostingKind kind, decimal amount,
        string reason, string intendedCodes, string? plan = null)
    {
        CompanyId = companyId;
        SourceKey = sourceKey;
        Kind = kind;
        Amount = amount;
        Reason = reason;
        IntendedCodes = intendedCodes;
        Plan = plan;
    }

    public void MarkResolved(DateTimeOffset at) => ResolvedAt = at;
    public void Stamp(DateTimeOffset at) => CreatedAt = at;

    /// <summary>Replaces the serialized replay plan (opaque to the domain). Used to refresh the plan's document
    /// reference when the payable's número da NF is edited while its incurral is still parked, so a later replay
    /// posts the corrected number. The Application layer owns the (de)serialization.</summary>
    public void SetPlan(string? plan) => Plan = plan;
}
