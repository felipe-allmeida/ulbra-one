namespace Ulbra.One.Finance.Application.Remittances;

/// <summary>
/// Port that parses a CNAB240 return (.RET) file into structured entries.
/// Pure: no I/O — the caller reads the file bytes and passes them here.
/// </summary>
public interface ICnabReturnParser
{
    /// <summary>
    /// Decode and parse a CNAB240 return file. Only Segmento-A detalhe records are extracted;
    /// all other record types (headers, trailers, Segmento B…) are skipped.
    /// </summary>
    /// <param name="file">Raw bytes of the .RET file (Latin-1 / ISO-8859-1 encoded).</param>
    IReadOnlyList<CnabReturnEntry> Parse(byte[] file);
}

/// <summary>
/// A single resolved payment entry extracted from a CNAB240 Segmento-A return record.
/// </summary>
/// <param name="Reference">
/// "Seu Número" (Segmento A, positions 74-93) — the company-assigned round-trip control id
/// echoed back unchanged from the remessa. Use this to match the return to an installment.
/// </param>
/// <param name="OccurrenceCodes">
/// Up to 5 two-character occurrence/return codes parsed from positions 231-240 of the Segmento A.
/// Blank and all-zero padding codes are dropped; only meaningful codes are included.
/// </param>
/// <param name="ActualValue">
/// "Valor Real de Efetivação" (Segmento A, positions 163-177) — the amount actually settled
/// by the bank (2 implied decimals). Zero when the payment was not yet settled.
/// </param>
/// <param name="ActualDate">
/// "Data Real de Efetivação" (Segmento A, positions 155-162) — the date the payment was
/// actually settled (DDMMAAAA). Null when not yet settled or field is blank/zeros.
/// </param>
/// <param name="BankReference">
/// "Nosso Número" (Segmento A, positions 135-154) — bank-assigned reference echoed on retorno.
/// Empty string when the bank did not fill the field.
/// </param>
public sealed record CnabReturnEntry(
    string Reference,
    IReadOnlyList<string> OccurrenceCodes,
    decimal ActualValue,
    DateOnly? ActualDate,
    string BankReference);
