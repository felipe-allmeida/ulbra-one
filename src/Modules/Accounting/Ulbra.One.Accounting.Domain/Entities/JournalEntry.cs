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
        JournalOrigin origin = JournalOrigin.Manual, string? sourceReference = null)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId required", nameof(companyId));
        CompanyId = companyId;
        CompetenceDate = competenceDate;
        Description = description?.Trim() ?? string.Empty;
        Origin = origin;
        SourceReference = string.IsNullOrWhiteSpace(sourceReference) ? null : sourceReference.Trim();
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
        if (_lines.Count < 2) throw new InvalidOperationException("A journal entry needs at least two lines.");
        if (_lines.Any(l => l.Amount <= 0)) throw new InvalidOperationException("All line amounts must be positive.");
        if (TotalDebit != TotalCredit) throw new InvalidOperationException("Debits and credits must balance.");

        Number = nextNumber;
        PostedAt = at;
        Status = JournalStatus.Posted;
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

        var reversal = new JournalEntry(CompanyId, competenceDate,
            $"Reversal of #{Number}: {trimmedReason}", Origin, SourceReference)
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
