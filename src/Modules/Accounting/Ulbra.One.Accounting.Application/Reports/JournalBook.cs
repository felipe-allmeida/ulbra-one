using Ulbra.One.Accounting.Domain;

namespace Ulbra.One.Accounting.Application.Reports;

/// <summary>Flat projection consumed by <see cref="JournalBookBuilder"/>: one posted/reversed journal line
/// joined to its ledger account's labels. One row per line (an entry spans several rows).</summary>
public sealed record PostedEntryLine(
    Guid EntryId,
    int? Number,
    DateOnly CompetenceDate,
    string Description,
    JournalStatus Status,
    Guid LedgerAccountId,
    string AccountCode,
    string AccountName,
    EntrySide Side,
    decimal Amount,
    Guid? CostCenterId);

/// <summary>One débito/crédito line of a Livro Diário entry, with its account labels.</summary>
public sealed record JournalBookLine(
    Guid LedgerAccountId,
    string AccountCode,
    string AccountName,
    EntrySide Side,
    decimal Amount,
    Guid? CostCenterId);

/// <summary>One scriptured entry (lançamento) in the Livro Diário, with its lines expanded and totals.</summary>
public sealed record JournalBookEntry(
    Guid Id,
    int? Number,
    DateOnly CompetenceDate,
    string Description,
    JournalStatus Status,
    IReadOnlyList<JournalBookLine> Lines,
    decimal TotalDebit,
    decimal TotalCredit);

/// <summary>All entries of a single competence day, with the day's debit/credit totals.</summary>
public sealed record JournalBookDay(
    DateOnly Date,
    IReadOnlyList<JournalBookEntry> Entries,
    decimal TotalDebit,
    decimal TotalCredit);

/// <summary>The Livro Diário: scriptured (Posted/Reversed) entries grouped by competence day, chronological.</summary>
public sealed record JournalBook(
    DateOnly? From,
    DateOnly? To,
    IReadOnlyList<JournalBookDay> Days,
    decimal TotalDebit,
    decimal TotalCredit);
