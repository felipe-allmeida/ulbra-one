using Ulbra.One.Accounting.Domain;

namespace Ulbra.One.Accounting.Application.Reports;

/// <summary>Flat projection consumed by <see cref="GeneralLedgerBuilder"/>: one Posted journal line of an
/// account joined to its account labels. One row per line (an account spans many rows). Carries the entry-level
/// metadata (number, competence, history, document ref) the Livro Razão movement rows need.</summary>
public sealed record GeneralLedgerPostedLine(
    Guid LedgerAccountId,
    string AccountCode,
    string? ReducedCode,
    string AccountName,
    NormalBalance NormalBalance,
    Guid EntryId,
    int? Number,
    DateOnly CompetenceDate,
    string EntryDescription,
    string? SourceReference,
    EntrySide Side,
    decimal Amount,
    Guid? CostCenterId,
    string? LineDescription,
    string? CounterpartyName = null,
    string? CounterpartyDocument = null);

/// <summary>One movement row of an account's Livro Razão: a posted line with its running accumulated balance
/// (signed by the account's <see cref="NormalBalance"/>).</summary>
public sealed record GeneralLedgerMovement(
    Guid EntryId,
    int? Number,
    DateOnly CompetenceDate,
    string History,
    string? DocumentRef,
    Guid? CostCenterId,
    decimal Debit,
    decimal Credit,
    decimal AccumulatedBalance,
    string? CounterpartyName = null,
    string? CounterpartyDocument = null);

/// <summary>One account section of the Livro Razão: the opening balance, the in-range movements with a running
/// balance, the period debit/credit totals and the closing balance. (Record name distinct from the
/// <c>LedgerAccount</c> entity.)</summary>
public sealed record GeneralLedgerAccount(
    Guid LedgerAccountId,
    string AccountCode,
    string? ReducedCode,
    string AccountName,
    NormalBalance NormalBalance,
    decimal OpeningBalance,
    IReadOnlyList<GeneralLedgerMovement> Movements,
    decimal TotalDebit,
    decimal TotalCredit,
    decimal ClosingBalance);
