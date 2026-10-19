using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>
/// Dry-run preview of a CNAB240 return file matched against a paying account's pending remittance
/// lines — nothing is persisted. Drives the "Retorno CNAB" reconciliation screen, where the operator
/// reviews which titles the bank paid/rejected before confirming the baixa line by line.
/// </summary>
[SwaggerSchema("Dry run of a CNAB return file against the pending remittance lines. NOTHING is "
    + "persisted — the operator reviews what the bank said before confirming the baixas.")]
public sealed record RemittanceReturnPreviewResponse(
    [property: SwaggerSchema("Whether the file's batch trailers add up. False means the file is refused "
        + "outright and no baixa is applied, however good the individual entries look.")]
    bool TrailerOk,
    [property: SwaggerSchema("What the trailer check found, when it failed.")]
    IReadOnlyList<string> TrailerMessages,
    [property: SwaggerSchema("Payer identity read from the file header, so the operator can confirm the "
        + "file belongs to the account they selected. Null when the header could not be read.")]
    RemittanceReturnPayer? Payer,
    [property: SwaggerSchema("How the entries break down across the outcome buckets.")]
    RemittanceReturnPreviewCounters Counters,
    [property: SwaggerSchema("The entries themselves, each matched or not to a remittance line.")]
    IReadOnlyList<RemittanceReturnPreviewEntry> Entries)
{
    /// <summary>How many "Paid" lines were auto-confirmed on upload (baixa aplicada). 0 for the read-only
    /// preview; on the auto-confirm upload it is the number of paid titles the system settled automatically.</summary>
    [SwaggerSchema("How many paid lines were settled automatically. Always 0 on the read-only preview; "
        + "on the auto-confirm upload it is what the system already booked without asking.")]
    public int AutoConfirmed { get; init; }
}

/// <summary>Payer identity read from the file header (tipo 0), for the operator to cross-check the
/// selected paying account against the file.</summary>
[SwaggerSchema("Payer identity from the return file header — for cross-checking the file against the "
    + "selected account.")]
public sealed record RemittanceReturnPayer(
    [property: SwaggerSchema("Payer CNPJ declared in the file, digits only.")]
    [property: SwaggerExample("88332580000165")]
    string Document,
    [property: SwaggerSchema("Branch declared in the file.")]
    [property: SwaggerExample("1234")]
    string Agency,
    [property: SwaggerSchema("Account declared in the file.")]
    [property: SwaggerExample("56789")]
    string Account);

/// <summary>Bucket counts across all entries in the file.</summary>
[SwaggerSchema("How the file's entries break down. Only Paid entries can be settled automatically; "
    + "everything else needs the operator.")]
public sealed record RemittanceReturnPreviewCounters(
    [property: SwaggerSchema("Entries in the file.")]
    [property: SwaggerExample("128")]
    int Total,
    [property: SwaggerSchema("Entries the bank reports as paid, matching the expected amount.")]
    [property: SwaggerExample("119")]
    int Paid,
    [property: SwaggerSchema("Entries the bank rejected. Confirming one releases the installment back to "
        + "open rather than settling it.")]
    [property: SwaggerExample("4")]
    int Rejected,
    [property: SwaggerSchema("Entries reported as paid but for a different amount than expected. Left "
        + "for manual review — the system will not guess which figure is right.")]
    [property: SwaggerExample("2")]
    int Divergent,
    [property: SwaggerSchema("Entries whose reference matches no known remittance line.")]
    [property: SwaggerExample("1")]
    int Unmatched,
    [property: SwaggerSchema("Entries that match more than one line, so the system cannot tell which.")]
    [property: SwaggerExample("0")]
    int Ambiguous,
    [property: SwaggerSchema("Entries already settled by an earlier import of the same file. Re-importing "
        + "is safe — these are simply skipped.")]
    [property: SwaggerExample("2")]
    int AlreadyProcessed);

/// <summary>
/// One return entry, matched (or not) to a pending remittance line. <c>ValorNominal</c> is the title
/// amount (from the matched line); <c>ValorEfetivado</c> is what the bank actually settled.
/// <c>Classification</c> and <c>MatchMethod</c> are the enum names as strings.
/// </summary>
[SwaggerSchema("One entry of the return file and what the system made of it. The two amounts are the "
    + "heart of it: what we asked for against what the bank actually settled.")]
public sealed record RemittanceReturnPreviewEntry(
    [property: SwaggerSchema("Our reference from the original remittance — how the entry is matched back.")]
    [property: SwaggerExample("PAG-0004821")]
    string Reference,
    [property: SwaggerSchema("Beneficiary name as it appears in the return file.")]
    [property: SwaggerExample("Construtora Horizonte Ltda")]
    string? FavoredName,
    [property: SwaggerSchema("Beneficiary CNPJ or CPF from the file, digits only.")]
    [property: SwaggerExample("12345678000195")]
    string? FavoredDocument,
    [property: SwaggerSchema("Payment method reported by the bank.")]
    [property: SwaggerExample("Ted")]
    string? Method,
    [property: SwaggerSchema("The title amount we expected, in BRL, taken from the matched line. Null "
        + "when nothing matched.")]
    [property: SwaggerExample("1430.25")]
    decimal? ValorNominal,
    [property: SwaggerSchema("What the bank actually settled, in BRL. A difference from ValorNominal is "
        + "what makes the entry Divergent.")]
    [property: SwaggerExample("1430.25")]
    decimal ValorEfetivado,
    [property: SwaggerSchema("Date the bank settled it.")]
    [property: SwaggerExample("2026-08-09")]
    DateOnly? EffectiveDate,
    [property: SwaggerSchema("Occurrence codes from the file — the bank's own vocabulary for what "
        + "happened.")]
    IReadOnlyList<string> OccurrenceCodes,
    [property: SwaggerSchema("Reason text decoded from the occurrence codes, when one could be decoded.")]
    [property: SwaggerExample("Conta do favorecido encerrada")]
    string? Motivo,
    [property: SwaggerSchema("Authentication code the bank returned for a settled payment.")]
    string? AuthCode,
    [property: SwaggerSchema("The bank's own reference for the entry.")]
    string? BankReference,
    [property: SwaggerSchema("What the system concluded: Paid, Rejected, Divergent, Unmatched, Ambiguous "
        + "or AlreadyProcessed. It decides what confirming this entry would do.")]
    [property: SwaggerExample("Paid")]
    string Classification,
    [property: SwaggerSchema("How the entry was matched — by our reference, or by a fallback. A weaker "
        + "match method is a reason to look closer before confirming.")]
    [property: SwaggerExample("Reference")]
    string MatchMethod,
    [property: SwaggerSchema("Remittance the matched line belongs to. Null when nothing matched.")]
    Guid? RemittanceId,
    [property: SwaggerSchema("The matched remittance line.")]
    Guid? LineId,
    [property: SwaggerSchema("Payable behind the matched line.")]
    Guid? PayableId,
    [property: SwaggerSchema("Installment behind the matched line — what a confirmation would settle.")]
    Guid? InstallmentId,
    [property: SwaggerSchema("Free-text note explaining a divergence or anything else worth flagging.")]
    string? Note);
