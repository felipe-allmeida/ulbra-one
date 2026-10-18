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

    // ---- Favored (vendor) — PIX copia e cola (AEL-963) -----------------------------------
    private const string DynamicQr = "00020101021226770014BR.GOV.BCB.PIX2555api.itau/pix/qr/v2/2b4a01c0-cb2a-4d71-8338-d3a4d8f5c8af5204000053039865802BR5915TUNA PAGAMENTOS6013FLORIANOPOLIS62070503***63049F31";

    [Fact]
    public void Favored_pix_valid_copia_e_cola_returns_null()
        => Assert.Null(CnabFieldValidation.FavoredReason(PaymentMethod.Pix, "11222333000181", Ted(), pixPayloadEmv: DynamicQr));

    [Fact]
    public void Favored_pix_malformed_copia_e_cola_is_rejected()
        => Assert.NotNull(CnabFieldValidation.FavoredReason(PaymentMethod.Pix, "11222333000181", Ted(), pixPayloadEmv: "000201notavalidbrcodexxxxxxxx"));

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

    // The agency check digit (dv_agencia) is a 1-char CNAB header field but the BankAccount column is
    // varchar(2). A stored 2-char value must be rejected here (as a diagnosable 422), not overflow the
    // writer as an unhandled 500 — mirrors the AccountDigit rule above.
    [Fact]
    public void Payer_agency_digit_too_long_is_rejected()
        => Assert.NotNull(CnabFieldValidation.PayerReason("11222333000181", "1234", "00012345", "6", agencyDigit: "12"));

    [Fact]
    public void Payer_agency_digit_single_char_is_accepted()
        => Assert.Null(CnabFieldValidation.PayerReason("11222333000181", "1234", "00012345", "6", agencyDigit: "9"));

    // ---- Boleto linha digitável --------------------------------------------------------

    [Fact]
    public void Boleto_bancario_47_digits_returns_null()
        => Assert.Null(CnabFieldValidation.BoletoReason("34191790010104351004791020150008291070026000123"));

    [Fact]
    public void Boleto_convenio_48_digits_starting_with_8_returns_null()
        => Assert.Null(CnabFieldValidation.BoletoReason("846700000017599200241114202410000000000000000000"));

    // A 48-digit line is only valid as arrecadação/convênio (starts with 8); without it, it is neither a
    // valid bancário (47) nor convênio — reject up front so BancarioTo44 never throws → HTTP 500.
    [Fact]
    public void Boleto_48_digits_without_the_8_prefix_is_rejected()
        => Assert.NotNull(CnabFieldValidation.BoletoReason("3" + new string('1', 47)));

    // A 47-digit line starting with 8 routes to convênio but ConvenioTo44 needs 48 → reject up front, else the
    // writer throws → HTTP 500.
    [Fact]
    public void Boleto_convenio_47_digits_starting_with_8_is_rejected()
        => Assert.NotNull(CnabFieldValidation.BoletoReason("82650000007889180798000002026177663112901001043"));

    [Fact]
    public void Boleto_wrong_length_is_rejected()
        => Assert.NotNull(CnabFieldValidation.BoletoReason("123"));
}
