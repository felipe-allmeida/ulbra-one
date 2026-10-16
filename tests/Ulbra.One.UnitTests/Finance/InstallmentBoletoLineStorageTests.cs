using Ulbra.One.Finance.Domain.Entities;
using Xunit;

namespace Ulbra.One.UnitTests.Finance;

/// <summary>The stored linha digitável is canonical (digits only), so the duplicate check can compare by
/// equality and the column can be indexed — a masked line and a bare one are the same boleto.</summary>
public class InstallmentBoletoLineStorageTests
{
    private const string Masked = "03399.12345 67890.123456 78901.234567 8 90120000010000";
    private const string Digits = "03399123456789012345678901234567890120000010000";

    [Fact]
    public void The_constructor_stores_the_line_as_digits()
        => Assert.Equal(Digits, new Installment(1, new DateOnly(2026, 9, 10), 100m, Masked).BoletoDigitableLine);

    [Fact]
    public void SetBoletoDigitableLine_stores_the_line_as_digits()
    {
        var installment = new Installment(1, new DateOnly(2026, 9, 10), 100m);

        installment.SetBoletoDigitableLine(Masked);

        Assert.Equal(Digits, installment.BoletoDigitableLine);
    }

    [Fact]
    public void A_blank_line_is_stored_as_null()
    {
        var installment = new Installment(1, new DateOnly(2026, 9, 10), 100m, "   ");

        Assert.Null(installment.BoletoDigitableLine);

        installment.SetBoletoDigitableLine(Masked);
        installment.SetBoletoDigitableLine("");

        Assert.Null(installment.BoletoDigitableLine);
    }
}
