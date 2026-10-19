namespace Ulbra.One.Accounting.Domain.Entities;

/// <summary>One side (débito or crédito) of a double-entry journal entry.</summary>
public sealed class JournalLine
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid JournalEntryId { get; private set; }
    public Guid LedgerAccountId { get; private set; }
    public EntrySide Side { get; private set; }
    public decimal Amount { get; private set; }
    public Guid? CostCenterId { get; private set; }
    public Guid? UnitId { get; private set; }
    public string? Description { get; private set; }

    private JournalLine() { }

    public JournalLine(Guid ledgerAccountId, EntrySide side, decimal amount,
        Guid? costCenterId = null, Guid? unitId = null, string? description = null)
    {
        if (ledgerAccountId == Guid.Empty) throw new ArgumentException("LedgerAccountId required", nameof(ledgerAccountId));
        if (amount <= 0) throw new ArgumentException("Line amount must be positive.", nameof(amount));
        LedgerAccountId = ledgerAccountId;
        Side = side;
        Amount = amount;
        CostCenterId = costCenterId;
        UnitId = unitId;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    /// <summary>Produces the mirror of this line on the opposite side — used when reversing an entry.</summary>
    internal JournalLine Mirror() =>
        new(LedgerAccountId, Side == EntrySide.Debit ? EntrySide.Credit : EntrySide.Debit,
            Amount, CostCenterId, UnitId, Description);
}
