using Ulbra.One.Common.Domain.Validation;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Cnab;
using Ulbra.One.Registry.Domain.ValueObjects;

namespace Ulbra.One.Finance.Application.Remittances;

/// <summary>
/// Validates that payer/favored data fits the fixed-width CNAB240 Santander fields, so malformed
/// data becomes a clean skip (per installment) or <c>Result</c> error (payer) instead of an
/// unhandled overflow deep inside <c>CnabRecordBuilder</c> (which surfaces as HTTP 500).
/// Widths mirror <c>SantanderCnabProfile</c> — see <c>docs/cnab240-layout.md</c>.
/// </summary>
public static class CnabFieldValidation
{
    private const int AgencyMaxDigits = 5;     // Seg A pos 24 (favored) / header pos 53 (payer)
    private const int AccountMaxDigits = 12;   // Seg A pos 30 (favored) / header pos 59 (payer)
    private const int AccountDigitMaxLen = 1;  // Seg A pos 42 / header pos 71 — Alpha, no truncation
    private const int AgencyDigitMaxLen = 1;   // header pos 58 (dv_agencia) — Alpha, no truncation
    private const int DocumentMaxDigits = 14;  // Seg B pos 19 (favored) / header pos 19 (payer CNPJ)

    /// <summary>Seg B pos 128-226 (X099) — the PIX key field width. Public + single source of truth: the
    /// writer (<c>SantanderCnabProfile.BuildSegmentB</c>) writes the key with this exact width and no
    /// truncation, so a key that passes this guard can never overflow/corrupt in the writer.</summary>
    public const int PixKeyMaxLen = 99;

    /// <summary>Reason the favored (vendor) data won't fit the CNAB fields, or <c>null</c> when it fits.
    /// Presence of bank/PIX data is checked by <see cref="RemittanceEligibility"/>; here we also require
    /// the favored document to be a valid CPF (11) or CNPJ (14) so the Seg B/J-52 inscription type is
    /// never silently written as isento ("0").</summary>
    public static string? FavoredReason(PaymentMethod method, string favoredDocument, BankInfo info,
        string? pixKeyOverride = null, string? pixPayloadEmv = null)
    {
        if (DigitCount(favoredDocument) is not (11 or 14))
            return "vendor document must be a valid CPF (11) or CNPJ (14)";

        if (method == PaymentMethod.Ted)
        {
            if (BankCodes.Normalize(info.BankCode).Length != 3)
                return "vendor bank code invalid for CNAB";
            if (DigitCount(info.Branch) > AgencyMaxDigits)
                return "vendor agency exceeds CNAB limit";
            if (DigitCount(info.Account) > AccountMaxDigits)
                return "vendor account exceeds CNAB limit";
            if (RawLength(info.AccountDigit) > AccountDigitMaxLen)
                return "vendor account digit exceeds CNAB limit";
        }
        else if (method == PaymentMethod.Pix)
        {
            // A per-payment copia-e-cola (AEL-963) is resolved: static → an embedded key (validated as a key
            // below), dynamic → a payload URL (fits Seg J52-PIX X079), anything malformed → a skip reason.
            if (!string.IsNullOrWhiteSpace(pixPayloadEmv))
            {
                var res = PixPayloadEmvResolver.Resolve(pixPayloadEmv);
                return res.Kind switch
                {
                    PixPayloadEmvKind.Invalid => res.Reason,
                    PixPayloadEmvKind.StaticKey => RawLength(res.PixKey) > PixKeyMaxLen ? "PIX key exceeds CNAB limit" : null,
                    _ => null, // DynamicQr — the URL length was checked by the resolver.
                };
            }

            // Validate the EFFECTIVE key actually written to Seg B: the per-payment override (AEL-938) when set,
            // else the vendor's registered key.
            var effectiveKey = string.IsNullOrWhiteSpace(pixKeyOverride) ? info.PixKey : pixKeyOverride;
            if (RawLength(effectiveKey) > PixKeyMaxLen)
                return "PIX key exceeds CNAB limit";
        }
        // Boleto: no vendor bank/PIX data is used (linha digitável validated via BoletoReason).

        return null;
    }

    /// <summary>Reason the boleto linha digitável is malformed, or <c>null</c>. The writer routes by
    /// <see cref="BoletoBarcode.IsConvenio"/> (lead <c>8</c>), so a convênio line MUST be 48 and a bancário 47 —
    /// reject a 47-with-8 (truncated convênio) and a 48-without-8 up front, else the converter throws → HTTP 500.</summary>
    public static string? BoletoReason(string? digitableLine)
    {
        var length = BoletoLine.Digits(digitableLine).Length;
        if (length is not (47 or 48))
            return "boleto linha digitável must be 47 (bancário) or 48 (convênio) digits";
        // Keep "boleto" in the reason: NothingToRemitDescription buckets the skip reason to pt-BR by that word.
        if (BoletoLine.IsConvenio(digitableLine) && length != 48)
            return "boleto convênio/arrecadação linha digitável must have 48 digits";
        if (!BoletoLine.IsConvenio(digitableLine) && length != 47)
            return "boleto linha digitável of 48 digits must be arrecadação/convênio (starts with 8)";
        return null;
    }

    /// <summary>Description of why the payer (company + paying account) data won't fit the CNAB header
    /// fields, or <c>null</c> when it fits. A bad payer blocks the whole remittance (not a per-line skip).</summary>
    public static string? PayerReason(string cnpj, string agency, string accountNumber, string accountDigit,
        string agencyDigit = "")
    {
        if (DigitCount(cnpj) > DocumentMaxDigits)
            return "Paying company CNPJ exceeds the CNAB field width.";
        if (DigitCount(agency) > AgencyMaxDigits)
            return "Paying account agency exceeds the CNAB field width.";
        if (RawLength(agencyDigit) > AgencyDigitMaxLen)
            return "Paying account agency check digit exceeds the CNAB field width.";
        if (DigitCount(accountNumber) > AccountMaxDigits)
            return "Paying account number exceeds the CNAB field width.";
        if (RawLength(accountDigit) > AccountDigitMaxLen)
            return "Paying account digit exceeds the CNAB field width.";
        return null;
    }

    private static int DigitCount(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return 0;
        var count = 0;
        foreach (var ch in value)
        {
            if (char.IsAsciiDigit(ch))
                count++;
        }
        return count;
    }

    private static int RawLength(string? value) => value?.Length ?? 0;
}
