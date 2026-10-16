namespace Ulbra.One.Finance.Application.Remittances;

/// <summary>
/// Port that parses a CNAB240 return (.RET) file into structured entries.
/// Pure: no I/O — the caller reads the file bytes and passes them here.
/// </summary>
public interface ICnabReturnParser
{
    /// <summary>
    /// Decode and parse a CNAB240 return file. Payment detalhe segments A (PIX/TED), J (boleto
    /// bancário) and O (convênio/tributo) are extracted; a following Segmento Z supplies the auth
    /// code. Headers, trailers and Segmento B are skipped.
    /// </summary>
    /// <param name="file">Raw bytes of the .RET file (Latin-1 / ISO-8859-1 encoded).</param>
    IReadOnlyList<CnabReturnEntry> Parse(byte[] file);

    /// <summary>
    /// Reconciles each lote's declared total (Trailer de Lote "Somatória dos Valores", tipo 5,
    /// positions 24-41) against the sum of its detail-record settled values — a file-integrity check
    /// that catches a truncated or corrupted return before baixas are applied.
    /// </summary>
    IReadOnlyList<CnabTrailerCheck> CheckTrailers(byte[] file);

    /// <summary>
    /// Reads the payer identity from the File Header (tipo 0) — CNPJ, agency and account. Used only to
    /// cross-check the paying bank account the operator selected (a mismatch is a warning, never a
    /// block). Returns null when no header record is present.
    /// </summary>
    CnabReturnHeader? ParseHeader(byte[] file);
}

/// <summary>Payer identity read from the CNAB240 File Header (tipo 0), for a paying-account cross-check.</summary>
/// <param name="PayerDocument">CNPJ pagador (positions 19-32, digits).</param>
/// <param name="Agency">Agência mantenedora (positions 53-57).</param>
/// <param name="AgencyDigit">DV da agência (position 58; blank when not informed).</param>
/// <param name="Account">Conta corrente (positions 59-70).</param>
public sealed record CnabReturnHeader(string PayerDocument, string Agency, string AgencyDigit, string Account);

/// <summary>Per-lote reconciliation of the Trailer de Lote declared total vs the summed detail values.</summary>
/// <param name="Lote">Lote number (positions 4-7).</param>
/// <param name="DeclaredTotal">"Somatória dos Valores" declared in the tipo-5 trailer.</param>
/// <param name="ActualTotal">Sum of the settled values across the lote's A/J/O detail records.</param>
public sealed record CnabTrailerCheck(string Lote, decimal DeclaredTotal, decimal ActualTotal)
{
    /// <summary>True when declared and actual differ by more than one cent.</summary>
    public bool IsDivergent => Math.Abs(DeclaredTotal - ActualTotal) > 0.01m;
}

/// <summary>
/// A single resolved payment entry extracted from a CNAB240 Segmento-A return record.
/// </summary>
/// <param name="Reference">
/// "Seu Número" — the company-assigned round-trip control id echoed back unchanged from the remessa
/// (Seg A 74-93, Seg J 183-202, Seg O 123-142). Use this to match the return to an installment.
/// </param>
/// <param name="OccurrenceCodes">
/// Up to 5 two-character occurrence/return codes parsed from positions 231-240.
/// Blank padding codes are dropped; only meaningful codes are included.
/// </param>
/// <param name="ActualValue">
/// The amount actually settled by the bank, 2 implied decimals (Seg A 163-177, Seg J 153-167,
/// Seg O 108-122). Zero when the payment was not yet settled.
/// </param>
/// <param name="ActualDate">
/// The date the payment was actually settled, DDMMAAAA (Seg A 155-162, Seg J 145-152, Seg O 100-107).
/// Null when not yet settled or field is blank/zeros.
/// </param>
/// <param name="BankReference">
/// "Nosso Número" — bank-assigned reference echoed on retorno (Seg A 135-154, Seg J 203-222,
/// Seg O 143-162). Empty string when the bank did not fill the field.
/// </param>
/// <param name="AuthCode">
/// Authentication code from a following Segmento Z (positions 15-78). Null when absent.
/// </param>
/// <param name="FavoredDocument">
/// Favored CPF/CNPJ from a following Segmento B (positions 19-32), for Seg A (PIX/TED) entries.
/// Used as a fallback match key when the bank altered/omitted "Seu Número". Null when absent.
/// </param>
/// <param name="FavoredPixKey">
/// Favored PIX key from a following Segmento B (Informação 12, positions 128-226), for Seg A PIX
/// entries. A fallback match key when the bank altered/omitted "Seu Número" and no CNPJ is present
/// (a PIX key transfer). Null when absent.
/// </param>
/// <param name="Barcode">
/// Código de barras (44 positions) from a Seg J (boleto bancário) or Seg O (convênio/tributo) return
/// record (positions 18-61). A fallback match key against the line's linha digitável when the bank
/// altered/omitted "Seu Número". Null for Seg A (PIX/TED) or when blank.
/// </param>
/// <param name="FavoredName">
/// Favored/cedente name echoed on the return (Seg A 44-73, Seg J/O 62-91). Shown by the crítica report.
/// Empty when blank.
/// </param>
/// <param name="LaunchValue">
/// "Valor do Lançamento" — the requested amount (Seg A 120-134, Seg J 100-114, Seg O 108-122), unlike
/// <see cref="ActualValue"/> (settled value). On a crítica the settled value is zero, so the report reads this.
/// </param>
public sealed record CnabReturnEntry(
    string Reference,
    IReadOnlyList<string> OccurrenceCodes,
    decimal ActualValue,
    DateOnly? ActualDate,
    string BankReference,
    string? AuthCode = null,
    string? FavoredDocument = null,
    string? FavoredPixKey = null,
    string? Barcode = null,
    string FavoredName = "",
    decimal LaunchValue = 0m);
