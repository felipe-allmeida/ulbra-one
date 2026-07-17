using Ulbra.One.Accounting.Domain;

namespace Ulbra.One.Accounting.Application.Reports;

/// <summary>A flattened posted journal line joined with its account's metadata — the pure input the
/// report calculators consume (no EF types). One per posted <c>JournalLine</c>. Carries the line's optional
/// cost center / unit so reports can filter by analytical dimension (used by the Livro Razão today; Balancete
/// and DRE inherit it). Both are trailing optional params so existing call sites stay unchanged.</summary>
public sealed record PostedLine(
    Guid LedgerAccountId,
    string AccountCode,
    string AccountName,
    AccountGroup AccountGroup,
    NormalBalance NormalBalance,
    EntrySide Side,
    decimal Amount,
    DateOnly CompetenceDate,
    Guid? CostCenterId = null,
    Guid? UnitId = null);
