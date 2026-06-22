using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application.Ids;

namespace Ulbra.One.Accounting.Application.Contracts;

/// <summary>One side (débito/crédito) of a journal entry.</summary>
public sealed record JournalLineResponse(
    JournalLineId Id,
    LedgerAccountId LedgerAccountId,
    EntrySide Side,
    decimal Amount,
    CostCenterId? CostCenterId,
    UnitId? UnitId,
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
public sealed record JournalEntryResponse(
    JournalEntryId Id,
    CompanyId CompanyId,
    int? Number,
    DateOnly CompetenceDate,
    string Description,
    JournalOrigin Origin,
    string? SourceReference,
    JournalStatus Status,
    DateTimeOffset? PostedAt,
    JournalEntryId? ReversalOfEntryId,
    JournalEntryId? ReversedByEntryId,
    decimal TotalDebit,
    decimal TotalCredit,
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
