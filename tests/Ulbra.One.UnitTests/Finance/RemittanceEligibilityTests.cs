using Ulbra.One.Finance.Application.Remittances;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Registry.Domain.ValueObjects;
using Xunit;

namespace Ulbra.One.UnitTests.Finance;

public class RemittanceEligibilityTests
{
    // ---------------------------------------------------------------------------
    // Test helpers
    // ---------------------------------------------------------------------------

    /// <summary>Builds a minimal <see cref="BankAccount"/> via the real public constructor.</summary>
    private static BankAccount TestBankAccount(
        string bankCode,
        string? agreement,
        PaymentMethod? defaultMethod)
    {
        var account = new BankAccount(
            companyId: Guid.NewGuid(),
            bankCode: bankCode,
            bankName: "Test Bank",
            agency: "0001",
            accountNumber: "12345",
            accountDigit: "6",
            accountType: BankAccountType.Checking,
            alias: "Test",
            openingBalance: null,
            cnabAgreementCode: agreement ?? "");

        if (defaultMethod.HasValue)
            account.SetAsDefaultPayer(defaultMethod.Value);

        return account;
    }

    private static BankAccount Santander(bool agreement = true) =>
        TestBankAccount(bankCode: "033", agreement: agreement ? "1234567" : null,
            defaultMethod: PaymentMethod.Pix);

    // Pix vendor: has PixKey (only required field for Pix).
    private static BankInfo ValidPix() => new("033", "0001", "12345", "6", "vendor@pix");

    // Ted vendor: has BankCode + Branch + Account (required for Ted).
    private static BankInfo ValidTed() => new("033", "0001", "12345", "6", PixKey: "");

    // ---------------------------------------------------------------------------
    // Happy path
    // ---------------------------------------------------------------------------

    [Fact]
    public void Santander_pix_with_pixkey_is_remittable()
        => Assert.Equal(RemittanceManualReason.None,
            RemittanceEligibility.Evaluate(Santander(), PaymentMethod.Pix, ValidPix()));

    [Fact]
    public void Santander_ted_with_bank_info_is_remittable()
        => Assert.Equal(RemittanceManualReason.None,
            RemittanceEligibility.Evaluate(
                TestBankAccount("033", "1234567", PaymentMethod.Ted),
                PaymentMethod.Ted,
                ValidTed()));

    // ---------------------------------------------------------------------------
    // Bank-level failures
    // ---------------------------------------------------------------------------

    [Fact]
    public void Non_santander_is_BankNotSupported()
        => Assert.Equal(RemittanceManualReason.BankNotSupported,
            RemittanceEligibility.Evaluate(
                TestBankAccount(bankCode: "341", agreement: "x", defaultMethod: PaymentMethod.Pix),
                PaymentMethod.Pix,
                ValidPix()));

    [Fact]
    public void Missing_agreement_is_MissingAgreementCode()
        => Assert.Equal(RemittanceManualReason.MissingAgreementCode,
            RemittanceEligibility.Evaluate(Santander(agreement: false), PaymentMethod.Pix, ValidPix()));

    // ---------------------------------------------------------------------------
    // Method failures
    // ---------------------------------------------------------------------------

    [Fact]
    public void Boleto_method_is_MethodNotSupported()
        => Assert.Equal(RemittanceManualReason.MethodNotSupported,
            RemittanceEligibility.Evaluate(Santander(), PaymentMethod.Boleto, ValidPix()));

    [Fact]
    public void Null_method_with_null_account_default_is_MethodNotSupported()
    {
        var account = TestBankAccount("033", "1234567", defaultMethod: null);
        Assert.Equal(RemittanceManualReason.MethodNotSupported,
            RemittanceEligibility.Evaluate(account, payableMethod: null, ValidPix()));
    }

    // ---------------------------------------------------------------------------
    // Vendor bank-info failures
    // ---------------------------------------------------------------------------

    [Fact]
    public void Empty_bankinfo_with_pix_is_VendorBankInfoMissing()
        => Assert.Equal(RemittanceManualReason.VendorBankInfoMissing,
            RemittanceEligibility.Evaluate(Santander(), PaymentMethod.Pix, BankInfo.Empty));

    [Fact]
    public void Missing_pix_key_is_VendorBankInfoMissing()
        => Assert.Equal(RemittanceManualReason.VendorBankInfoMissing,
            RemittanceEligibility.Evaluate(Santander(), PaymentMethod.Pix,
                new BankInfo("033", "0001", "12345", "6", PixKey: "")));

    [Fact]
    public void Ted_missing_bank_code_is_VendorBankInfoMissing()
        => Assert.Equal(RemittanceManualReason.VendorBankInfoMissing,
            RemittanceEligibility.Evaluate(
                TestBankAccount("033", "1234567", PaymentMethod.Ted),
                PaymentMethod.Ted,
                new BankInfo(BankCode: "", Branch: "0001", Account: "12345", AccountDigit: "6", PixKey: "")));

    [Fact]
    public void Ted_missing_branch_is_VendorBankInfoMissing()
        => Assert.Equal(RemittanceManualReason.VendorBankInfoMissing,
            RemittanceEligibility.Evaluate(
                TestBankAccount("033", "1234567", PaymentMethod.Ted),
                PaymentMethod.Ted,
                new BankInfo(BankCode: "033", Branch: "", Account: "12345", AccountDigit: "6", PixKey: "")));

    [Fact]
    public void Ted_missing_account_is_VendorBankInfoMissing()
        => Assert.Equal(RemittanceManualReason.VendorBankInfoMissing,
            RemittanceEligibility.Evaluate(
                TestBankAccount("033", "1234567", PaymentMethod.Ted),
                PaymentMethod.Ted,
                new BankInfo(BankCode: "033", Branch: "0001", Account: "", AccountDigit: "6", PixKey: "")));

    // ---------------------------------------------------------------------------
    // IsRemittable convenience wrapper
    // ---------------------------------------------------------------------------

    [Fact]
    public void IsRemittable_returns_true_when_None()
        => Assert.True(RemittanceEligibility.IsRemittable(Santander(), PaymentMethod.Pix, ValidPix()));

    [Fact]
    public void IsRemittable_returns_false_when_not_eligible()
        => Assert.False(RemittanceEligibility.IsRemittable(
            TestBankAccount("341", "x", PaymentMethod.Pix), PaymentMethod.Pix, ValidPix()));

    // ---------------------------------------------------------------------------
    // Pix via account default method
    // ---------------------------------------------------------------------------

    [Fact]
    public void Null_payable_method_falls_back_to_account_default_pix()
        => Assert.Equal(RemittanceManualReason.None,
            RemittanceEligibility.Evaluate(Santander(), payableMethod: null, ValidPix()));
}
