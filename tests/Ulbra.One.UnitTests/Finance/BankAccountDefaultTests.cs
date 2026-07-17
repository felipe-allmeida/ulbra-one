using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.UnitTests.Finance;

public class BankAccountDefaultTests
{
    private static BankAccount New() => new(Guid.NewGuid(), "001", "BB", "1234", "5678", "9",
        BankAccountType.Checking, "Conta", null);

    [Fact]
    public void SetAsDefaultPayer_sets_flag_and_method()
    {
        var b = New();
        b.SetAsDefaultPayer(PaymentMethod.Pix);
        Assert.True(b.IsDefaultForPayments);
        Assert.Equal(PaymentMethod.Pix, b.DefaultPaymentMethod);
    }

    [Fact]
    public void ClearDefaultPayer_resets()
    {
        var b = New();
        b.SetAsDefaultPayer(PaymentMethod.Ted);
        b.ClearDefaultPayer();
        Assert.False(b.IsDefaultForPayments);
        Assert.Null(b.DefaultPaymentMethod);
    }

    [Theory]
    [InlineData("33", "033")]
    [InlineData("0033", "033")]
    [InlineData(" 033 ", "033")]
    [InlineData("341", "341")]
    public void Constructor_normalizes_bank_code(string input, string expected)
    {
        var b = new BankAccount(Guid.NewGuid(), input, "Santander", "1234", "5678", "9",
            BankAccountType.Checking, "Conta", null);
        Assert.Equal(expected, b.BankCode);
    }

    [Fact]
    public void Update_normalizes_bank_code()
    {
        var b = New();
        b.Update("33", "Santander", "1234", "5678", "9", BankAccountType.Checking, "Conta", null);
        Assert.Equal("033", b.BankCode);
    }
}
