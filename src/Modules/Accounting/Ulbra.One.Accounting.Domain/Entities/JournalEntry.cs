using Ulbra.One.Common.Domain.Auditing;

namespace Ulbra.One.Accounting.Domain.Entities;

/// <summary>A double-entry journal entry (lançamento) aggregate: a balanced set of debit/credit lines
/// posted against a company's ledger. Draft is editable; once posted it is immutable and corrected
/// only via <see cref="Reverse"/>.</summary>
public sealed class JournalEntry : IAuditable, ISoftDeletable
{
    private readonly List<JournalLine> _lines = new();

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CompanyId { get; private set; }

    /// <summary>Sequential, per-company entry number assigned on Post. Null while Draft.</summary>
    public int? Number { get; private set; }

    /// <summary>Accounting competence (regime de competência) the entry belongs to.</summary>
    public DateOnly CompetenceDate { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public JournalOrigin Origin { get; private set; }
    public string? SourceReference { get; private set; }
    /// <summary>Idempotency key for system-generated entries (e.g. "payable-incurral:xyz"). Null for manual entries.</summary>
    public string? SourceKey { get; private set; }
    public JournalStatus Status { get; private set; } = JournalStatus.Draft;
    public DateTimeOffset? PostedAt { get; private set; }

    /// <summary>When this entry is itself a reversal, the id of the entry it reverses.</summary>
    public Guid? ReversalOfEntryId { get; private set; }

    /// <summary>When this (posted) entry has been reversed, the id of the reversal entry.</summary>
    public Guid? ReversedByEntryId { get; private set; }

    // IAuditable
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    // ISoftDeletable
    public DateTimeOffset? DeletedAt { get; private set; }

    public IReadOnlyList<JournalLine> Lines => _lines;

    private JournalEntry() { }

    public JournalEntry(Guid companyId, DateOnly competenceDate, string description,
        JournalOrigin origin = JournalOrigin.Manual, string? sourceReference = null, string? sourceKey = null)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId required", nameof(companyId));
        CompanyId = companyId;
        CompetenceDate = competenceDate;
        Description = description?.Trim() ?? string.Empty;
        Origin = origin;
        SourceReference = string.IsNullOrWhiteSpace(sourceReference) ? null : sourceReference.Trim();
        SourceKey = string.IsNullOrWhiteSpace(sourceKey) ? null : sourceKey.Trim();
    }

    public decimal TotalDebit => _lines.Where(l => l.Side == EntrySide.Debit).Sum(l => l.Amount);
    public decimal TotalCredit => _lines.Where(l => l.Side == EntrySide.Credit).Sum(l => l.Amount);
    public bool IsBalanced => _lines.Count > 0 && TotalDebit == TotalCredit;

    /// <summary>Updates Draft header fields. Posted/Reversed entries are immutable.</summary>
    public void UpdateHeader(DateOnly competenceDate, string description, string? sourceReference)
    {
        EnsureDraft();
        CompetenceDate = competenceDate;
        Description = description?.Trim() ?? string.Empty;
        SourceReference = string.IsNullOrWhiteSpace(sourceReference) ? null : sourceReference.Trim();
    }

    public JournalLine AddLine(JournalLine line)
    {
        EnsureDraft();
        ArgumentNullException.ThrowIfNull(line);
        _lines.Add(line);
        return line;
    }

    public void RemoveLine(Guid lineId)
    {
        EnsureDraft();
        var line = _lines.FirstOrDefault(l => l.Id == lineId)
            ?? throw new InvalidOperationException("Line not found on this entry.");
        _lines.Remove(line);
    }

    /// <summary>Validates and posts the entry, freezing it. <paramref name="nextNumber"/> is the next
    /// sequential number for the company, computed by the caller (kept out of the aggregate for purity).</summary>
    public void Post(int nextNumber, DateTimeOffset at)
    {
        EnsureDraft();
        if (nextNumber < 1) throw new ArgumentException("Entry number must be positive.", nameof(nextNumber));
        ValidateStructure();

        Number = nextNumber;
        PostedAt = at;
        Status = JournalStatus.Posted;
    }

    /// <summary>The single source of truth for the journal-entry structural invariants
    /// (≥2 lines, all amounts positive, Σdebit==Σcredit). Throws <see cref="JournalEntryException"/>
    /// carrying a stable <see cref="JournalEntryErrors"/> code on the first violation. Used at Post
    /// time (and via <see cref="Reverse"/>). The composition rules a draft must satisfy while still
    /// editable are a subset, exposed by <see cref="ValidateDraftComposition"/>.</summary>
    public void ValidateStructure()
    {
        ValidateDraftComposition();
        if (TotalDebit != TotalCredit) throw new JournalEntryException(JournalEntryErrors.Unbalanced);
    }

    /// <summary>The structural rules a draft must already satisfy while still editable: ≥2 lines and
    /// all amounts positive. Balancing (Σdebit==Σcredit) is intentionally NOT enforced here — a draft
    /// may be saved unbalanced and balanced before Post (see <see cref="ValidateStructure"/>). Throws
    /// <see cref="JournalEntryException"/> with a stable code; called by the create/update endpoints so
    /// the same rule always yields the same code as Post — never a second copy.</summary>
    public void ValidateDraftComposition()
    {
        if (_lines.Count < 2) throw new JournalEntryException(JournalEntryErrors.TooFewLines);
        if (_lines.Any(l => l.Amount <= 0)) throw new JournalEntryException(JournalEntryErrors.NonPositiveAmount);
    }

    /// <summary>Reverses this posted entry, producing a new balanced Posted entry with mirrored lines.
    /// This entry transitions to Reversed and the two are linked. <paramref name="nextNumber"/> is the
    /// sequential number assigned to the reversal entry.</summary>
    public JournalEntry Reverse(string reason, DateOnly competenceDate, int nextNumber, DateTimeOffset at)
    {
        if (Status != JournalStatus.Posted)
            throw new InvalidOperationException("Only a posted entry can be reversed.");

        var trimmedReason = reason?.Trim();
        if (string.IsNullOrEmpty(trimmedReason))
            throw new ArgumentException("A reversal reason is required.", nameof(reason));

        var reversalSourceKey = SourceKey is not null ? $"{SourceKey}:reversal" : null;
        var reversal = new JournalEntry(CompanyId, competenceDate,
            $"Reversal of #{Number}: {trimmedReason}", Origin, SourceReference, reversalSourceKey)
        {
            ReversalOfEntryId = Id,
        };
        foreach (var line in _lines)
            reversal.AddLine(line.Mirror());
        reversal.Post(nextNumber, at);

        Status = JournalStatus.Reversed;
        ReversedByEntryId = reversal.Id;
        return reversal;
    }

    private void EnsureDraft()
    {
        if (Status != JournalStatus.Draft)
            throw new InvalidOperationException("Only a draft entry can be modified.");
    }

    public void MarkCreated(string userId, DateTimeOffset at) { CreatedBy = userId; CreatedAt = at; MarkUpdated(userId, at); }
    public void MarkUpdated(string userId, DateTimeOffset at) { UpdatedBy = userId; UpdatedAt = at; }
    public void MarkDeleted(DateTimeOffset at) => DeletedAt = at;
}
