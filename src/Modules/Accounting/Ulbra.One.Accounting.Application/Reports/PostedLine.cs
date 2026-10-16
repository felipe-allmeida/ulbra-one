using Ulbra.One.Accounting.Domain;

namespace Ulbra.One.Accounting.Application.Reports;

/// <summary>A flattened posted journal line joined with its account's metadata — the pure input the
/// report calculators consume (no EF types). One per posted <c>JournalLine</c>. Carries the line's optional
/// cost center / unit so reports can filter by analytical dimension (Livro Razão today; Balancete and DRE
/// inherit it), the account's reduced code (código resumido) for compact display, and its
/// <see cref="Domain.DreLine"/> so the DRE can build its cascade, and the entry's <see cref="JournalOrigin"/>
/// so the DRE can exclude year-end <c>Closing</c> entries (which would otherwise zero the result). All trailing
/// params are optional so existing call sites stay unchanged.</summary>
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
    Guid? UnitId = null,
    string? ReducedCode = null,
    DreLine? DreLine = null,
    JournalOrigin Origin = JournalOrigin.Manual);
