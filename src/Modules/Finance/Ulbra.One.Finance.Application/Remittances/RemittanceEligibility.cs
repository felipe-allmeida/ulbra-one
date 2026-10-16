using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Registry.Domain.ValueObjects;

namespace Ulbra.One.Finance.Application.Remittances;

public enum RemittanceManualReason
{
    None,
    BankNotSupported,
    MissingAgreementCode,
    MethodNotSupported,
    VendorBankInfoMissing,
    /// <summary>Boleto has no linha digitável — off the auto rail until it is filled.</summary>
    BoletoLineMissing,
    /// <summary>Boleto has a linha digitável but malformed (wrong length / convênio not 48). Distinct from
    /// <see cref="BoletoLineMissing"/> so the UI says "confira a linha", not "informe a linha".</summary>
    BoletoLineInvalid,
    /// <summary>PIX copia-e-cola present but malformed (bad EMV structure/CRC, or a static QR with no key) —
    /// the remittance would skip it, so the rail must show it off-rail with the right reason (AEL-963).</summary>
    PixPayloadEmvInvalid,
    /// <summary>PIX copia-e-cola is a valid dynamic QR but forma 47 is gated off (EnablePixQrCode=false) —
    /// off-rail until homologação; distinct from "invalid" so the UI explains it's pending, not broken.</summary>
    PixQrDisabled,
}

/// <summary>Single source of truth for whether an installment can ride the automatic CNAB remittance rail.
/// Shared by the daily sweep, the manual generate endpoint, and the payment read model.</summary>
public static class RemittanceEligibility
{
    /// <summary>
    /// Evaluates whether the combination of paying account, payment method, and vendor bank info
    /// is eligible for CNAB remittance.
    /// </summary>
    /// <remarks>
    /// Bank-info validity per method mirrors <c>RemittanceGenerationService</c> exactly:
    /// <list type="bullet">
    ///   <item><b>Ted</b>: requires non-empty <c>BankCode</c>, <c>Branch</c>, and <c>Account</c> on the vendor.</item>
    ///   <item><b>Pix</b>: requires non-empty <c>PixKey</c> on the vendor (branch/account alone is NOT sufficient).</item>
    /// </list>
    /// </remarks>
    public static RemittanceManualReason Evaluate(
        BankAccount payingAccount,
        PaymentMethod? method,
        BankInfo vendorBankInfo,
        string? boletoDigitableLine = null,
        string? pixKeyOverride = null,
        string? pixPayloadEmv = null,
        bool enablePixQrCode = true)
    {
        if (!BankCodes.IsSantander(payingAccount.BankCode))
            return RemittanceManualReason.BankNotSupported;

        if (string.IsNullOrEmpty(payingAccount.CnabAgreementCode))
            return RemittanceManualReason.MissingAgreementCode;

        // The method is the one resolved per installment/payable (AEL-933) — no account default anymore.
        if (method is not (PaymentMethod.Pix or PaymentMethod.Ted or PaymentMethod.Boleto))
            return RemittanceManualReason.MethodNotSupported;

        // Validate bank info per method — mirrors RemittanceGenerationService logic exactly.
        if (method == PaymentMethod.Ted)
        {
            if (string.IsNullOrEmpty(vendorBankInfo.BankCode)
                || string.IsNullOrEmpty(vendorBankInfo.Branch)
                || string.IsNullOrEmpty(vendorBankInfo.Account))
                return RemittanceManualReason.VendorBankInfoMissing;
        }
        else if (method == PaymentMethod.Pix)
        {
            // A copia-e-cola (AEL-963) supplies the destination itself — resolve it here so the rail agrees
            // with the generation service: malformed → off-rail (Invalid), a valid dynamic QR with the gate
            // off → off-rail (QrDisabled), otherwise (static key / enabled dynamic) → eligible.
            if (!string.IsNullOrWhiteSpace(pixPayloadEmv))
            {
                var res = PixPayloadEmvResolver.Resolve(pixPayloadEmv);
                return res.Kind switch
                {
                    PixPayloadEmvKind.Invalid => RemittanceManualReason.PixPayloadEmvInvalid,
                    PixPayloadEmvKind.DynamicQr when !enablePixQrCode => RemittanceManualReason.PixQrDisabled,
                    _ => RemittanceManualReason.None,
                };
            }

            // No copia-e-cola: a per-payment PIX override (AEL-938) or the vendor's registered key is required.
            if (string.IsNullOrEmpty(pixKeyOverride) && string.IsNullOrEmpty(vendorBankInfo.PixKey))
                return RemittanceManualReason.VendorBankInfoMissing;
        }
        else if (method == PaymentMethod.Boleto)
        {
            // Boleto rides no vendor bank/PIX data — but it DOES require the linha digitável (on the installment).
            // Split "missing" from "present-but-malformed": a blank line is BoletoLineMissing, a filled but
            // invalid one (wrong length / convênio not 48) is BoletoLineInvalid. Both keep it off the auto rail.
            if (string.IsNullOrWhiteSpace(boletoDigitableLine))
                return RemittanceManualReason.BoletoLineMissing;
            if (CnabFieldValidation.BoletoReason(boletoDigitableLine) is not null)
                return RemittanceManualReason.BoletoLineInvalid;
        }

        return RemittanceManualReason.None;
    }

    public static bool IsRemittable(
        BankAccount payingAccount,
        PaymentMethod? method,
        BankInfo vendorBankInfo,
        string? boletoDigitableLine = null,
        string? pixKeyOverride = null,
        string? pixPayloadEmv = null,
        bool enablePixQrCode = true)
        => Evaluate(payingAccount, method, vendorBankInfo, boletoDigitableLine, pixKeyOverride, pixPayloadEmv, enablePixQrCode) == RemittanceManualReason.None;
}
