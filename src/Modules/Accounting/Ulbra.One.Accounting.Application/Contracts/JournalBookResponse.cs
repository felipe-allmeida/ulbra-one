using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Common.Application.Ids;

namespace Ulbra.One.Accounting.Application.Contracts;

/// <summary>One débito/crédito line of a Livro Diário entry, with the account's code and name.</summary>
public sealed record JournalBookLineResponse(
    LedgerAccountId LedgerAccountId,
    string AccountCode,
    string AccountName,
    EntrySide Side,
    decimal Amount,
    CostCenterId? CostCenterId)
{
    public static JournalBookLineResponse From(JournalBookLine l) => new(
        new LedgerAccountId(l.LedgerAccountId), l.AccountCode, l.AccountName, l.Side, l.Amount,
        l.CostCenterId is { } cc ? new CostCenterId(cc) : null);
}

/// <summary>One scriptured entry (lançamento) in the Livro Diário with its lines expanded.</summary>
public sealed record JournalBookEntryResponse(
    JournalEntryId Id,
    int? Number,
    DateOnly CompetenceDate,
    string Description,
    JournalStatus Status,
    IReadOnlyList<JournalBookLineResponse> Lines,
    decimal TotalDebit,
    decimal TotalCredit)
{
    public static JournalBookEntryResponse From(JournalBookEntry e) => new(
        new JournalEntryId(e.Id), e.Number, e.CompetenceDate, e.Description, e.Status,
        e.Lines.Select(JournalBookLineResponse.From).ToList(), e.TotalDebit, e.TotalCredit);
}

/// <summary>All entries of a single competence day, with the day's totals.</summary>
public sealed record JournalBookDayResponse(
    DateOnly Date,
    IReadOnlyList<JournalBookEntryResponse> Entries,
    decimal TotalDebit,
    decimal TotalCredit)
{
    public static JournalBookDayResponse From(JournalBookDay d) => new(
        d.Date, d.Entries.Select(JournalBookEntryResponse.From).ToList(), d.TotalDebit, d.TotalCredit);
}

/// <summary>The Livro Diário (read-only) for a company — or consolidated when <paramref name="CompanyId"/> is null.</summary>
public sealed record JournalBookResponse(
    CompanyId? CompanyId,
    DateOnly? From,
    DateOnly? To,
    IReadOnlyList<JournalBookDayResponse> Days,
    decimal TotalDebit,
    decimal TotalCredit)
{
    public static JournalBookResponse Of(JournalBook book, CompanyId? companyId) => new(
        companyId, book.From, book.To,
        book.Days.Select(JournalBookDayResponse.From).ToList(), book.TotalDebit, book.TotalCredit);
}
