using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Accounting.Application.Endpoints.JournalEntries;

/// <summary>Wire shape for one debit/credit line when creating or editing a draft journal entry.</summary>
public sealed record JournalLineInput
{
    [SwaggerSchema("Ledger account this line hits. It must be a posting account — a synthetic (grouping) "
        + "account cannot receive entries.")]
    public LedgerAccountId LedgerAccountId { get; set; }

    [SwaggerSchema("Whether the line debits or credits the account. The entry only posts when total "
        + "debits equal total credits.")]
    public EntrySide Side { get; set; }

    [SwaggerSchema("Line amount in BRL, always positive — the direction comes from Side.")]
    [SwaggerExample("1250.00")]
    public decimal Amount { get; set; }

    [SwaggerSchema("Cost center to charge this line to. Optional, and validated against the entry's "
        + "company when supplied.")]
    public CostCenterId? CostCenterId { get; set; }

    [SwaggerSchema("Unit to charge this line to. Optional, and validated against the entry's company "
        + "when supplied.")]
    public UnitId? UnitId { get; set; }

    [SwaggerSchema("Per-line note. Falls back to the entry's description when omitted.")]
    public string? Description { get; set; }
}
