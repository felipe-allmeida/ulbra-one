using Ulbra.One.Finance.Domain;
using Ulbra.One.Registry.Domain.ValueObjects;

namespace Ulbra.One.Finance.Application.Remittances;

/// <summary>
/// Validates that payer/favored data fits the fixed-width CNAB240 Santander fields, so malformed
/// data becomes a clean skip (per installment) or <c>Result</c> error (payer) instead of an
/// unhandled overflow deep inside <c>CnabRecordBuilder</c> (which surfaces as HTTP 500).
/// Widths mirror <c>SantanderCnabProfile</c> — see <c>.superpowers/sdd/cnab240-layout.md</c>.
/// </summary>
public static class CnabFieldValidation
{
    private const int AgencyMaxDigits = 5;     // Seg A pos 24 (favored) / header pos 53 (payer)
    private const int AccountMaxDigits = 12;   // Seg A pos 30 (favored) / header pos 59 (payer)
    private const int AccountDigitMaxLen = 1;  // Seg A pos 42 / header pos 71 — Alpha, no truncation
    private const int DocumentMaxDigits = 14;  // Seg B pos 19 (favored) / header pos 19 (payer CNPJ)
    private const int PixKeyMaxLen = 99;       // Seg B pos 128-226 — truncation would corrupt the key

    /// <summary>Reason the favored (vendor) data won't fit the CNAB fields, or <c>null</c> when it fits.
    /// Assumes presence was already checked by <see cref="RemittanceEligibility"/>.</summary>
    public static string? FavoredReason(PaymentMethod method, string favoredDocument, BankInfo info)
    {
        if (DigitCount(favoredDocument) > DocumentMaxDigits)
            return "vendor document exceeds CNAB limit";

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
        else // Pix
        {
            if (RawLength(info.PixKey) > PixKeyMaxLen)
                return "vendor PIX key exceeds CNAB limit";
        }

        return null;
    }

    /// <summary>Description of why the payer (company + paying account) data won't fit the CNAB header
    /// fields, or <c>null</c> when it fits. A bad payer blocks the whole remittance (not a per-line skip).</summary>
    public static string? PayerReason(string cnpj, string agency, string accountNumber, string accountDigit)
    {
        if (DigitCount(cnpj) > DocumentMaxDigits)
            return "Paying company CNPJ exceeds the CNAB field width.";
        if (DigitCount(agency) > AgencyMaxDigits)
            return "Paying account agency exceeds the CNAB field width.";
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
