using Swashbuckle.AspNetCore.Annotations;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>
/// Reading of a Santander CRÍTICA file — the return that only reports whether each entry was accepted
/// (acatado) or rejected in the bank's validation. Unlike a movimento file it settles nothing: this
/// report never applies a baixa. The operator decides, title by title, to remove rejected ones from the batch.
/// </summary>
[SwaggerSchema("Reading of a Santander crítica file (accept/reject). Applies NO baixa — it is only the "
    + "bank's verdict on the remittance validation.")]
public sealed record RemittanceCriticismResponse(
    [property: SwaggerSchema("Payer identity from the file header, to cross-check the selected account. "
        + "Null when the header could not be read.")]
    RemittanceReturnPayer? Payer,
    [property: SwaggerSchema("Total detail records (A/J/O) in the crítica file.")]
    int Total,
    [property: SwaggerSchema("How many were accepted (occurrence 00/03/BD/BE/BF or blank).")]
    int Success,
    [property: SwaggerSchema("How many were rejected in the crítica (any other occurrence).")]
    int Rejection,
    [property: SwaggerSchema("The records, each matched (when possible) to its remittance line to enable "
        + "removing it from the batch.")]
    IReadOnlyList<RemittanceCriticismRecord> Records);

/// <summary>One crítica record, matched (when possible) to a remittance line by "Seu Número".</summary>
[SwaggerSchema("One entry of the crítica file and the bank's verdict on it.")]
public sealed record RemittanceCriticismRecord(
    [property: SwaggerSchema("\"Seu Número\" — the remittance reference echoed by the bank.")]
    string Reference,
    [property: SwaggerSchema("Favored/cedente name (from the matched line when available, else the file).")]
    string? FavoredName,
    [property: SwaggerSchema("Entry amount (on a crítica the settled value is zero, so the Valor do Lançamento is used).")]
    decimal Amount,
    [property: SwaggerSchema("Occurrence codes read from positions 231-240.")]
    IReadOnlyList<string> OccurrenceCodes,
    [property: SwaggerSchema("Reason decoded from the codes, when rejected.")]
    string? Motivo,
    [property: SwaggerSchema("True = accepted; false = rejected in the crítica.")]
    bool IsSuccess,
    [property: SwaggerSchema("\"Nosso Número\" assigned by the bank, when present.")]
    string? BankReference,
    [property: SwaggerSchema("Remittance of the matched line. Null when nothing matched by reference.")]
    Guid? RemittanceId,
    [property: SwaggerSchema("Matched remittance line — target of the \"remove from batch\" action.")]
    Guid? LineId,
    [property: SwaggerSchema("Status of the matched line (Pending/Paid/Returned) — only Pending is still in the batch.")]
    string? LineStatus);
