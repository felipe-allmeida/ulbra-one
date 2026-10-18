using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Accounting.Application.Contracts;

/// <summary>One side (débito/crédito) of a journal entry.</summary>
[SwaggerSchema("One side of a journal entry — a single debit or credit against one account.")]
public sealed record JournalLineResponse(
    JournalLineId Id,
    [property: SwaggerSchema("Account posted to. Must be an analytical account.")]
    LedgerAccountId LedgerAccountId,
    [property: SwaggerSchema("Whether this line is the debit or the credit.")]
    EntrySide Side,
    [property: SwaggerSchema("Line amount, in BRL. Always positive — the direction comes from Side, not "
        + "from the sign.")]
    [property: SwaggerExample("4500.00")]
    decimal Amount,
    [property: SwaggerSchema("Cost center the line is charged to, for management reporting. Optional and "
        + "outside double entry — it does not affect the balance.")]
    CostCenterId? CostCenterId,
    [property: SwaggerSchema("Unit the line is charged to. Same as the cost center: analytical only.")]
    UnitId? UnitId,
    [property: SwaggerSchema("Line-level note, when it adds something the entry description does not.")]
    [property: SwaggerExample("Nota 12345 — parcela 1/3")]
    string? Description)
{
    public static JournalLineResponse From(JournalLine l) => new(
        new JournalLineId(l.Id),
        new LedgerAccountId(l.LedgerAccountId),
        l.Side,
        l.Amount,
        l.CostCenterId is { } cc ? new CostCenterId(cc) : null,
        l.UnitId is { } u ? new UnitId(u) : null,
        l.Description);
}

/// <summary>A double-entry journal entry (lançamento) with its lines.</summary>
[SwaggerSchema("A double-entry journal entry (lançamento) with its lines. Debits and credits must "
    + "balance before it can be posted, and once posted it is immutable — a mistake is undone by a "
    + "reversal entry, never by editing.")]
public sealed record JournalEntryResponse(
    JournalEntryId Id,
    [property: SwaggerSchema("Company the entry belongs to.")]
    CompanyId CompanyId,
    [property: SwaggerSchema("Sequential number within the company, assigned on posting. Null while the "
        + "entry is still a draft — a draft has no number to cite.")]
    [property: SwaggerExample("4821")]
    int? Number,
    [property: SwaggerSchema("Competence date, which decides the accounting period the entry lands in. If "
        + "that period is closed the posting is clamped forward rather than refused.")]
    [property: SwaggerExample("2026-07-31")]
    DateOnly CompetenceDate,
    [property: SwaggerSchema("What the entry records — the history line the accountant reads.")]
    [property: SwaggerExample("Provisão de despesa — Construtora Horizonte, NF 12345")]
    string Description,
    [property: SwaggerSchema("What produced the entry: a person posting by hand, or an automatic posting "
        + "from a payable, a payment or the year-end close.")]
    JournalOrigin Origin,
    [property: SwaggerSchema("Reference back to what generated the entry, when Origin is automatic — the "
        + "invoice number of the payable behind it, for example.")]
    [property: SwaggerExample("NF 12345")]
    string? SourceReference,
    [property: SwaggerSchema("Whether the entry is still a draft or already posted. Only a draft can be "
        + "edited.")]
    JournalStatus Status,
    [property: SwaggerSchema("When the entry was posted. Null while it is a draft.")]
    DateTimeOffset? PostedAt,
    [property: SwaggerSchema("The entry this one reverses, when it IS a reversal. Present together with "
        + "the original's ReversedByEntryId — the pair is what shows the correction in the ledger.")]
    JournalEntryId? ReversalOfEntryId,
    [property: SwaggerSchema("The reversal that undid this entry, when it HAS been reversed. A reversed "
        + "entry stays in the ledger with its lines intact; it is the pair that nets to zero.")]
    JournalEntryId? ReversedByEntryId,
    [property: SwaggerSchema("Sum of the debit lines, in BRL. Equal to TotalCredit on any postable entry "
        + "— they differ only on an unbalanced draft.")]
    [property: SwaggerExample("4500.00")]
    decimal TotalDebit,
    [property: SwaggerSchema("Sum of the credit lines, in BRL.")]
    [property: SwaggerExample("4500.00")]
    decimal TotalCredit,
    [property: SwaggerSchema("The lines themselves. At least two, since a single-sided entry cannot "
        + "balance.")]
    IReadOnlyList<JournalLineResponse> Lines)
{
    public static JournalEntryResponse From(JournalEntry e) => new(
        new JournalEntryId(e.Id),
        new CompanyId(e.CompanyId),
        e.Number,
        e.CompetenceDate,
        e.Description,
        e.Origin,
        e.SourceReference,
        e.Status,
        e.PostedAt,
        e.ReversalOfEntryId is { } r ? new JournalEntryId(r) : null,
        e.ReversedByEntryId is { } rb ? new JournalEntryId(rb) : null,
        e.TotalDebit,
        e.TotalCredit,
        e.Lines.Select(JournalLineResponse.From).ToList());
}
