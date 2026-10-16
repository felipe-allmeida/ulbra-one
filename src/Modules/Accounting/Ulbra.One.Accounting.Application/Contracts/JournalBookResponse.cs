using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Accounting.Application.Contracts;

/// <summary>One débito/crédito line of a Livro Diário entry, with the account's code and name.</summary>
[SwaggerSchema("One line of a Livro Diário entry, with the account already resolved to code and name.")]
public sealed record JournalBookLineResponse(
    LedgerAccountId LedgerAccountId,
    [property: SwaggerSchema("Hierarchical account code.")]
    [property: SwaggerExample("3.1.2.04")]
    string AccountCode,
    [property: SwaggerSchema("Account name.")]
    [property: SwaggerExample("Manutenção e conservação predial")]
    string AccountName,
    [property: SwaggerSchema("Whether the line is the debit or the credit.")]
    EntrySide Side,
    [property: SwaggerSchema("Line amount, in BRL. Always positive — the direction is in Side.")]
    [property: SwaggerExample("4500.00")]
    decimal Amount,
    [property: SwaggerSchema("Cost center tagged on the line.")]
    CostCenterId? CostCenterId)
{
    public static JournalBookLineResponse From(JournalBookLine l) => new(
        new LedgerAccountId(l.LedgerAccountId), l.AccountCode, l.AccountName, l.Side, l.Amount,
        l.CostCenterId is { } cc ? new CostCenterId(cc) : null);
}

/// <summary>One scriptured entry (lançamento) in the Livro Diário with its lines expanded.</summary>
[SwaggerSchema("One entry as it appears in the Livro Diário, with its lines expanded.")]
public sealed record JournalBookEntryResponse(
    JournalEntryId Id,
    [property: SwaggerSchema("Sequential number, assigned on posting. Null on a draft.")]
    [property: SwaggerExample("4821")]
    int? Number,
    [property: SwaggerSchema("Competence date, which is what groups the entry into its day.")]
    [property: SwaggerExample("2026-07-31")]
    DateOnly CompetenceDate,
    [property: SwaggerSchema("What the entry records.")]
    [property: SwaggerExample("Provisão de despesa — Construtora Horizonte, NF 12345")]
    string Description,
    [property: SwaggerSchema("Whether the entry is posted or still a draft.")]
    JournalStatus Status,
    [property: SwaggerSchema("The entry's lines.")]
    IReadOnlyList<JournalBookLineResponse> Lines,
    [property: SwaggerSchema("Sum of the debit lines, in BRL.")]
    [property: SwaggerExample("4500.00")]
    decimal TotalDebit,
    [property: SwaggerSchema("Sum of the credit lines, in BRL.")]
    [property: SwaggerExample("4500.00")]
    decimal TotalCredit)
{
    public static JournalBookEntryResponse From(JournalBookEntry e) => new(
        new JournalEntryId(e.Id), e.Number, e.CompetenceDate, e.Description, e.Status,
        e.Lines.Select(JournalBookLineResponse.From).ToList(), e.TotalDebit, e.TotalCredit);
}

/// <summary>All entries of a single competence day, with the day's totals.</summary>
[SwaggerSchema("A single competence day of the Livro Diário, with the day's totals.")]
public sealed record JournalBookDayResponse(
    [property: SwaggerSchema("The competence date these entries share.")]
    [property: SwaggerExample("2026-07-31")]
    DateOnly Date,
    [property: SwaggerSchema("Entries posted on the day.")]
    IReadOnlyList<JournalBookEntryResponse> Entries,
    [property: SwaggerSchema("Debits posted on the day, in BRL.")]
    [property: SwaggerExample("184000.00")]
    decimal TotalDebit,
    [property: SwaggerSchema("Credits posted on the day, in BRL.")]
    [property: SwaggerExample("184000.00")]
    decimal TotalCredit)
{
    public static JournalBookDayResponse From(JournalBookDay d) => new(
        d.Date, d.Entries.Select(JournalBookEntryResponse.From).ToList(), d.TotalDebit, d.TotalCredit);
}

/// <summary>The Livro Diário (read-only) for a company — or consolidated when <paramref name="CompanyId"/> is null.</summary>
[SwaggerSchema("The Livro Diário — every entry in the range, grouped by competence day. Read-only: it "
    + "is a view of the ledger, and nothing is written through it.")]
public sealed record JournalBookResponse(
    [property: SwaggerSchema("Company the book covers. Null means consolidated.")]
    CompanyId? CompanyId,
    [property: SwaggerSchema("Start of the range. Null means from the beginning of history.")]
    [property: SwaggerExample("2026-07-01")]
    DateOnly? From,
    [property: SwaggerSchema("End of the range, inclusive. Null means up to the latest entry.")]
    [property: SwaggerExample("2026-07-31")]
    DateOnly? To,
    [property: SwaggerSchema("Days with at least one entry, in chronological order. A day with no "
        + "movement is omitted rather than returned empty.")]
    IReadOnlyList<JournalBookDayResponse> Days,
    [property: SwaggerSchema("Debits over the whole range, in BRL.")]
    [property: SwaggerExample("1250000.00")]
    decimal TotalDebit,
    [property: SwaggerSchema("Credits over the whole range, in BRL.")]
    [property: SwaggerExample("1250000.00")]
    decimal TotalCredit)
{
    public static JournalBookResponse Of(JournalBook book, CompanyId? companyId) => new(
        companyId, book.From, book.To,
        book.Days.Select(JournalBookDayResponse.From).ToList(), book.TotalDebit, book.TotalCredit);
}
