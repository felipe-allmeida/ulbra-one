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

        // AEL-933: the account no longer carries a method; the flag only marks it as a default payer. The
        // `defaultMethod` param is kept as a "is default payer" marker for call-site brevity — its value is unused.
        if (defaultMethod.HasValue)
            account.SetAsDefaultPayer();

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

    // Boleto não exige dados bancários do fornecedor, MAS exige a linha digitável (na parcela) — sem ela o
    // gerador descartaria o título silenciosamente, então a elegibilidade a sinaliza como pendência.
    [Fact]
    public void Boleto_with_a_valid_bancario_line_is_remittable_without_vendor_bank_info()
        => Assert.Equal(RemittanceManualReason.None,
            RemittanceEligibility.Evaluate(Santander(), PaymentMethod.Boleto, BankInfo.Empty,
                "34191790010104351004791020150008291070026000123")); // 47 digits (bancário)

    [Fact]
    public void Boleto_with_a_valid_convenio_line_is_remittable()
        => Assert.Equal(RemittanceManualReason.None,
            RemittanceEligibility.Evaluate(Santander(), PaymentMethod.Boleto, BankInfo.Empty,
                "846700000017599200241114202410000000000000000000")); // 48 digits (convênio)

    [Fact]
    public void Boleto_without_a_digitable_line_is_BoletoLineMissing()
        => Assert.Equal(RemittanceManualReason.BoletoLineMissing,
            RemittanceEligibility.Evaluate(Santander(), PaymentMethod.Boleto, BankInfo.Empty, boletoDigitableLine: null));

    [Fact]
    public void Boleto_with_a_blank_digitable_line_is_BoletoLineMissing()
        => Assert.Equal(RemittanceManualReason.BoletoLineMissing,
            RemittanceEligibility.Evaluate(Santander(), PaymentMethod.Boleto, BankInfo.Empty, boletoDigitableLine: "   "));

    // A present-but-malformed line is distinct from "missing" (UI: "confira a linha", not "informe a linha").
    // Wrong length and a 47-digit convênio both land here.
    [Fact]
    public void Boleto_with_a_wrong_length_line_is_BoletoLineInvalid()
        => Assert.Equal(RemittanceManualReason.BoletoLineInvalid,
            RemittanceEligibility.Evaluate(Santander(), PaymentMethod.Boleto, BankInfo.Empty, "12345")); // != 47/48

    [Fact]
    public void Boleto_convenio_47_digits_is_BoletoLineInvalid()
        => Assert.Equal(RemittanceManualReason.BoletoLineInvalid,
            RemittanceEligibility.Evaluate(Santander(), PaymentMethod.Boleto, BankInfo.Empty,
                "82650000007889180798000002026177663112901001043")); // 47 digits, lead 8 (truncated convênio)

    [Fact]
    public void Null_method_is_MethodNotSupported()
    {
        var account = TestBankAccount("033", "1234567", defaultMethod: null);
        Assert.Equal(RemittanceManualReason.MethodNotSupported,
            RemittanceEligibility.Evaluate(account, method: null, ValidPix()));
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

    // AEL-938: a per-payment PIX override keeps the installment eligible even when the vendor has no key
    // registered — the exact use case (pay this account to a different key just this once).
    [Fact]
    public void Pix_override_supplies_the_key_when_vendor_has_none()
        => Assert.Equal(RemittanceManualReason.None,
            RemittanceEligibility.Evaluate(Santander(), PaymentMethod.Pix,
                new BankInfo("033", "0001", "12345", "6", PixKey: ""), pixKeyOverride: "pagamentos@fornecedor.com.br"));

    [Fact]
    public void Pix_with_neither_override_nor_vendor_key_is_VendorBankInfoMissing()
        => Assert.Equal(RemittanceManualReason.VendorBankInfoMissing,
            RemittanceEligibility.Evaluate(Santander(), PaymentMethod.Pix, BankInfo.Empty, pixKeyOverride: null));

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
}
