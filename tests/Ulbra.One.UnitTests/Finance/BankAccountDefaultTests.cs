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
}
