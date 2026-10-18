using Ulbra.One.Common.Domain.Validation;

namespace Ulbra.One.Finance.Domain.Cnab;

/// <summary>
/// Converts a boleto/arrecadação <em>linha digitável</em> into the 44-position <em>código de
/// barras</em> that the CNAB240 Segmento J (boleto bancário) and Segmento O (convênio/tributo)
/// carry at positions 18-61.
///
/// Pure, stateless — no I/O. The conversion is a structural reorder/strip only: it never computes
/// check digits, it reuses the DVs already present in the linha digitável. DV <em>verification</em>
/// (mod-10/11 consistency) is a 🔶 homologação follow-up — the bank validates the barcode on its
/// side, and <c>CnabFieldValidation</c> (Application) pre-screens length/format so malformed input
/// becomes a clean skip instead of an overflow.
///
/// Lives in the Domain so both the remittance writer (Infrastructure) and the return matcher
/// (Application) can reuse it — the conversion is pure business logic, not infrastructure.
///
/// See <c>docs/cnab240-layout.md</c> (Seg J / Seg O) and the reference conversion in
/// <c>santanderCnab.ts:convertLinhaDigitavelToCodigoBarras</c>.
/// </summary>
public static class BoletoBarcode
{
    /// <summary>
    /// True when the linha digitável is an arrecadação/convênio document (código de barras starts
    /// with <c>8</c>), routing it to Segmento O; otherwise it is a boleto bancário (Segmento J + J-52).
    /// Delegates to the canonical <see cref="BoletoLine.IsConvenio"/>; the 47/48-digit validation lives
    /// in CnabFieldValidation.BoletoReason (Application).
    /// </summary>
    public static bool IsConvenio(string? linhaDigitavel) => BoletoLine.IsConvenio(linhaDigitavel);

    /// <summary>
    /// Dispatches to <see cref="ConvenioTo44"/> or <see cref="BancarioTo44"/> based on
    /// <see cref="IsConvenio"/>, returning the 44-position código de barras.
    /// </summary>
    public static string ToBarcode(string? linhaDigitavel) =>
        IsConvenio(linhaDigitavel) ? ConvenioTo44(linhaDigitavel) : BancarioTo44(linhaDigitavel);

    /// <summary>
    /// Boleto bancário: 47-digit linha digitável → 44-position código de barras. Drops the three
    /// per-field DVs (positions 10, 21, 32 in 1-based terms) and reorders the fields into the
    /// FEBRABAN barcode order: banco+moeda, DV geral, fator de vencimento + valor, then the three
    /// campos livres. A value already 44 digits long is returned as-is.
    /// </summary>
    public static string BancarioTo44(string? linhaDigitavel)
    {
        var ld = Digits(linhaDigitavel);
        if (ld.Length == 44)
            return ld;
        if (ld.Length != 47)
            throw new ArgumentException(
                $"Linha digitável de boleto bancário deve ter 47 dígitos (recebido: {ld.Length}).",
                nameof(linhaDigitavel));

        // 0-based slices of the 47-digit linha digitável.
        var bancoMoeda = ld.Substring(0, 4);   // banco (3) + moeda (1)
        var campoLivre1 = ld.Substring(4, 5);  // pos 10 (index 9) is field-1 DV — skipped
        var campoLivre2 = ld.Substring(10, 10); // pos 21 (index 20) is field-2 DV — skipped
        var campoLivre3 = ld.Substring(21, 10); // pos 32 (index 31) is field-3 DV — skipped
        var dvGeral = ld.Substring(32, 1);
        var fatorValor = ld.Substring(33, 14);  // fator vencimento (4) + valor (10)

        return bancoMoeda + dvGeral + fatorValor + campoLivre1 + campoLivre2 + campoLivre3;
    }

    /// <summary>
    /// Convênio/arrecadação: 48-digit linha digitável → 44-position código de barras. The linha is
    /// four blocks of 12 (11 significant digits + 1 DV each); the barcode is the four 11-digit
    /// significant blocks concatenated. A value already 44 digits long is returned as-is.
    /// </summary>
    public static string ConvenioTo44(string? linhaDigitavel)
    {
        var ld = Digits(linhaDigitavel);
        if (ld.Length == 44)
            return ld;
        if (ld.Length != 48)
            throw new ArgumentException(
                $"Linha digitável de convênio/arrecadação deve ter 48 dígitos (recebido: {ld.Length}).",
                nameof(linhaDigitavel));

        // Four blocks of 12 → take the first 11 (significant) of each, drop the trailing DV.
        return ld.Substring(0, 11) + ld.Substring(12, 11) + ld.Substring(24, 11) + ld.Substring(36, 11);
    }

    // Delegates to the canonical ASCII digit extraction so routing (IsConvenio) and conversion agree on
    // the exact digit set — a Unicode non-ASCII digit must not make them disagree (would throw → HTTP 500).
    private static string Digits(string? value) => BoletoLine.Digits(value);
}
