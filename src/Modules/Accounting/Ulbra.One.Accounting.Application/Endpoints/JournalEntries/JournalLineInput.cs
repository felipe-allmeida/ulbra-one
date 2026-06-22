using Ulbra.One.Accounting.Domain;
using Ulbra.One.Common.Application.Ids;

namespace Ulbra.One.Accounting.Application.Endpoints.JournalEntries;

/// <summary>Wire shape for one debit/credit line when creating or editing a draft journal entry.</summary>
public sealed record JournalLineInput
{
    public LedgerAccountId LedgerAccountId { get; set; }
    public EntrySide Side { get; set; }
    public decimal Amount { get; set; }
    public CostCenterId? CostCenterId { get; set; }
    public UnitId? UnitId { get; set; }
    public string? Description { get; set; }
}
