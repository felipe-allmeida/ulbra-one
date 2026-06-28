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
        PaymentMethod? payableMethod,
        BankInfo vendorBankInfo)
    {
        if (payingAccount.BankCode != "033")
            return RemittanceManualReason.BankNotSupported;

        if (string.IsNullOrEmpty(payingAccount.CnabAgreementCode))
            return RemittanceManualReason.MissingAgreementCode;

        var method = payableMethod ?? payingAccount.DefaultPaymentMethod;

        if (method is not (PaymentMethod.Pix or PaymentMethod.Ted))
            return RemittanceManualReason.MethodNotSupported;

        // Validate bank info per method — mirrors RemittanceGenerationService logic exactly.
        if (method == PaymentMethod.Ted)
        {
            if (string.IsNullOrEmpty(vendorBankInfo.BankCode)
                || string.IsNullOrEmpty(vendorBankInfo.Branch)
                || string.IsNullOrEmpty(vendorBankInfo.Account))
                return RemittanceManualReason.VendorBankInfoMissing;
        }
        else // Pix
        {
            if (string.IsNullOrEmpty(vendorBankInfo.PixKey))
                return RemittanceManualReason.VendorBankInfoMissing;
        }

        return RemittanceManualReason.None;
    }

    public static bool IsRemittable(
        BankAccount payingAccount,
        PaymentMethod? payableMethod,
        BankInfo vendorBankInfo)
        => Evaluate(payingAccount, payableMethod, vendorBankInfo) == RemittanceManualReason.None;
}
