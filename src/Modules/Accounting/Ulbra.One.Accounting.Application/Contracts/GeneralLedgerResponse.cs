using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Accounting.Application.Contracts;

/// <summary>One movement row of the Livro Razão, with its running accumulated balance.</summary>
[SwaggerSchema("One movement of the Livro Razão, carrying the running balance after it. Debit and "
    + "credit are separate columns, and exactly one of them is non-zero on any row.")]
public sealed record GeneralLedgerMovementResponse(
    JournalEntryId EntryId,
    [property: SwaggerSchema("Sequential number of the entry. Null if it is still a draft.")]
    [property: SwaggerExample("4821")]
    int? Number,
    [property: SwaggerSchema("Competence date of the entry.")]
    [property: SwaggerExample("2026-07-31")]
    DateOnly CompetenceDate,
    [property: SwaggerSchema("The história — what the entry records.")]
    [property: SwaggerExample("Provisão de despesa — Construtora Horizonte, NF 12345")]
    string History,
    [property: SwaggerSchema("Reference to the source document, when the entry came from one.")]
    [property: SwaggerExample("NF 12345")]
    string? DocumentRef,
    [property: SwaggerSchema("Cost center tagged on the line, for management reporting.")]
    CostCenterId? CostCenterId,
    [property: SwaggerSchema("Debit amount on this row, in BRL. Zero when the row is a credit.")]
    [property: SwaggerExample("4500.00")]
    decimal Debit,
    [property: SwaggerSchema("Credit amount on this row, in BRL. Zero when the row is a debit.")]
    [property: SwaggerExample("0.00")]
    decimal Credit,
    [property: SwaggerSchema("Balance after this movement, in BRL, signed by the account's normal "
        + "balance. It is cumulative from the section's opening balance, so it only makes sense in the "
        + "order the rows come in.")]
    [property: SwaggerExample("16500.00")]
    decimal AccumulatedBalance,
    [property: SwaggerSchema("Counterparty on the source document, denormalized so the ledger reads "
        + "without opening each entry.")]
    [property: SwaggerExample("Construtora Horizonte Ltda")]
    string? CounterpartyName,
    [property: SwaggerSchema("Counterparty CNPJ or CPF, digits only.")]
    [property: SwaggerExample("12345678000195")]
    string? CounterpartyDocument)
{
    public static GeneralLedgerMovementResponse From(GeneralLedgerMovement m) => new(
        new JournalEntryId(m.EntryId), m.Number, m.CompetenceDate, m.History, m.DocumentRef,
        m.CostCenterId is { } cc ? new CostCenterId(cc) : null,
        m.Debit, m.Credit, m.AccumulatedBalance, m.CounterpartyName, m.CounterpartyDocument);
}

/// <summary>One account section of the Livro Razão: code (+ reduced code), opening balance, movements with a
/// running balance, period debit/credit totals and the closing balance.</summary>
[SwaggerSchema("One account's section of the Livro Razão: where it started, every movement in order, "
    + "and where it ended.")]
public sealed record GeneralLedgerAccountResponse(
    LedgerAccountId LedgerAccountId,
    [property: SwaggerSchema("Hierarchical account code.")]
    [property: SwaggerExample("3.1.2.04")]
    string AccountCode,
    [property: SwaggerSchema("Short code used by the accountants, when the account has one.")]
    [property: SwaggerExample("41204")]
    string? ReducedCode,
    [property: SwaggerSchema("Account name.")]
    [property: SwaggerExample("Manutenção e conservação predial")]
    string AccountName,
    [property: SwaggerSchema("The side that increases this account — it signs the balances below.")]
    NormalBalance NormalBalance,
    [property: SwaggerSchema("Balance carried in from before the range, in BRL. The running balance on "
        + "the first movement starts from here, not from zero.")]
    [property: SwaggerExample("12000.00")]
    decimal OpeningBalance,
    [property: SwaggerSchema("Movements in the range, in chronological order — the order matters, since "
        + "each row's accumulated balance depends on the ones before it.")]
    IReadOnlyList<GeneralLedgerMovementResponse> Movements,
    [property: SwaggerSchema("Debits in the range, in BRL.")]
    [property: SwaggerExample("4500.00")]
    decimal TotalDebit,
    [property: SwaggerSchema("Credits in the range, in BRL.")]
    [property: SwaggerExample("0.00")]
    decimal TotalCredit,
    [property: SwaggerSchema("Balance at the end of the range, in BRL.")]
    [property: SwaggerExample("16500.00")]
    decimal ClosingBalance)
{
    public static GeneralLedgerAccountResponse From(GeneralLedgerAccount a) => new(
        new LedgerAccountId(a.LedgerAccountId), a.AccountCode, a.ReducedCode, a.AccountName, a.NormalBalance,
        a.OpeningBalance, a.Movements.Select(GeneralLedgerMovementResponse.From).ToList(),
        a.TotalDebit, a.TotalCredit, a.ClosingBalance);
}
