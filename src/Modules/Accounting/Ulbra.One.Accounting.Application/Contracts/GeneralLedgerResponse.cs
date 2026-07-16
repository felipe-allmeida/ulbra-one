using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Common.Application.Ids;

namespace Ulbra.One.Accounting.Application.Contracts;

/// <summary>One movement row of the Livro Razão, with its running accumulated balance.</summary>
public sealed record GeneralLedgerMovementResponse(
    JournalEntryId EntryId,
    int? Number,
    DateOnly CompetenceDate,
    string History,
    string? DocumentRef,
    CostCenterId? CostCenterId,
    decimal Debit,
    decimal Credit,
    decimal AccumulatedBalance)
{
    public static GeneralLedgerMovementResponse From(GeneralLedgerMovement m) => new(
        new JournalEntryId(m.EntryId), m.Number, m.CompetenceDate, m.History, m.DocumentRef,
        m.CostCenterId is { } cc ? new CostCenterId(cc) : null,
        m.Debit, m.Credit, m.AccumulatedBalance);
}

/// <summary>One account section of the Livro Razão: código (+ código reduzido), opening balance, movements with
/// running balance, período débito/crédito totals and the closing balance.</summary>
public sealed record GeneralLedgerAccountResponse(
    LedgerAccountId LedgerAccountId,
    string AccountCode,
    string? ReducedCode,
    string AccountName,
    NormalBalance NormalBalance,
    decimal OpeningBalance,
    IReadOnlyList<GeneralLedgerMovementResponse> Movements,
    decimal TotalDebit,
    decimal TotalCredit,
    decimal ClosingBalance)
{
    public static GeneralLedgerAccountResponse From(GeneralLedgerAccount a) => new(
        new LedgerAccountId(a.LedgerAccountId), a.AccountCode, a.ReducedCode, a.AccountName, a.NormalBalance,
        a.OpeningBalance, a.Movements.Select(GeneralLedgerMovementResponse.From).ToList(),
        a.TotalDebit, a.TotalCredit, a.ClosingBalance);
}
