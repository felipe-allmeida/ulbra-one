using Ulbra.One.Finance.Application.Remittances;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Registry.Domain.ValueObjects;
using Xunit;

namespace Ulbra.One.UnitTests.Finance;

public class CnabFieldValidationTests
{
    private static BankInfo Ted(string bankCode = "341", string branch = "0001",
        string account = "123456", string digit = "7") =>
        new(bankCode, branch, account, digit, PixKey: "");

    // ---- Favored (vendor) — TED ----------------------------------------------------------

    [Fact]
    public void Favored_ted_valid_returns_null()
        => Assert.Null(CnabFieldValidation.FavoredReason(PaymentMethod.Ted, "11222333000181", Ted()));

    [Fact]
    public void Favored_ted_account_too_long_is_rejected()
        => Assert.NotNull(CnabFieldValidation.FavoredReason(
            PaymentMethod.Ted, "11222333000181", Ted(account: "1234567890123"))); // 13 digits

    [Fact]
    public void Favored_ted_agency_too_long_is_rejected()
        => Assert.NotNull(CnabFieldValidation.FavoredReason(
            PaymentMethod.Ted, "11222333000181", Ted(branch: "123456"))); // 6 digits

    [Fact]
    public void Favored_ted_account_digit_too_long_is_rejected()
        => Assert.NotNull(CnabFieldValidation.FavoredReason(
            PaymentMethod.Ted, "11222333000181", Ted(digit: "12")));

    [Fact]
    public void Favored_ted_non_numeric_bank_code_is_rejected()
        => Assert.NotNull(CnabFieldValidation.FavoredReason(
            PaymentMethod.Ted, "11222333000181", Ted(bankCode: "ABC")));

    [Fact]
    public void Favored_ted_leading_zero_bank_code_is_accepted()
        => Assert.Null(CnabFieldValidation.FavoredReason(
            PaymentMethod.Ted, "11222333000181", Ted(bankCode: "0033"))); // normalizes to 033

    [Fact]
    public void Favored_document_too_long_is_rejected()
        => Assert.NotNull(CnabFieldValidation.FavoredReason(
            PaymentMethod.Ted, "112223330001810", Ted())); // 15 digits

    // ---- Favored (vendor) — PIX ----------------------------------------------------------

    [Fact]
    public void Favored_pix_valid_returns_null()
        => Assert.Null(CnabFieldValidation.FavoredReason(
            PaymentMethod.Pix, "11222333000181", new BankInfo("", "", "", "", "vendor@pix.com")));

    [Fact]
    public void Favored_pix_key_too_long_is_rejected()
        => Assert.NotNull(CnabFieldValidation.FavoredReason(
            PaymentMethod.Pix, "11222333000181", new BankInfo("", "", "", "", new string('a', 100))));

    // ---- Payer -------------------------------------------------------------------------

    [Fact]
    public void Payer_valid_returns_null()
        => Assert.Null(CnabFieldValidation.PayerReason("11222333000181", "1234", "00012345", "6"));

    [Fact]
    public void Payer_cnpj_too_long_is_rejected()
        => Assert.NotNull(CnabFieldValidation.PayerReason("112223330001810", "1234", "00012345", "6"));

    [Fact]
    public void Payer_agency_too_long_is_rejected()
        => Assert.NotNull(CnabFieldValidation.PayerReason("11222333000181", "123456", "00012345", "6"));

    [Fact]
    public void Payer_account_too_long_is_rejected()
        => Assert.NotNull(CnabFieldValidation.PayerReason("11222333000181", "1234", "1234567890123", "6"));

    [Fact]
    public void Payer_account_digit_too_long_is_rejected()
        => Assert.NotNull(CnabFieldValidation.PayerReason("11222333000181", "1234", "00012345", "66"));
}
