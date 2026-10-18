using Ulbra.One.Registry.Domain.Entities;
using Ulbra.One.Registry.Domain.ValueObjects;

namespace Ulbra.One.UnitTests.Registry;

public class VendorEntityTests
{
    private static VendorEntity NewVendor() => new(
        personType: PersonType.Company,
        name: "Gráfica Sul Ltda", tradeName: "Gráfica Sul", document: "11.222.333/0001-81",
        email: "contato@gs.com", phone: "5133334444", address: Address.Empty,
        paymentTerms: "30 dias",
        bankInfo: new BankInfo("001", "1234", "56789", "6", ""),
        taxRegime: TaxRegime.SimplesNacional,
        paymentSchedule: PaymentSchedule.Default);

    [Fact]
    public void New_Vendor_Is_Active_With_Normalized_Document()
    {
        var v = NewVendor();
        Assert.True(v.Active);
        Assert.Equal("11222333000181", v.Document);
        Assert.NotEqual(Guid.Empty, v.Id);
        Assert.Null(v.DeletedAt);
    }

    [Fact]
    public void Update_Changes_Mutable_Fields()
    {
        var v = NewVendor();
        v.Update(PersonType.Company, "Novo Nome", "NN", "11222333000181",
            "novo@x.com", "5199998888", Address.Empty, "à vista", BankInfo.Empty, TaxRegime.Unknown,
            PaymentSchedule.Default);
        Assert.Equal("Novo Nome", v.Name);
        Assert.Equal("à vista", v.PaymentTerms);
        Assert.Equal(BankInfo.Empty, v.BankInfo);
    }

    [Fact]
    public void Deactivate_And_Activate_Toggle_Active()
    {
        var v = NewVendor();
        v.Deactivate();
        Assert.False(v.Active);
        v.Activate();
        Assert.True(v.Active);
    }

    [Fact]
    public void MarkDeleted_Sets_DeletedAt()
    {
        var v = NewVendor();
        var at = new DateTimeOffset(2026, 6, 4, 0, 0, 0, TimeSpan.Zero);
        v.MarkDeleted(at);
        Assert.Equal(at, v.DeletedAt);
    }

    [Fact]
    public void New_Vendor_Defaults_To_No_Associated_Ledger_Accounts()
    {
        Assert.Empty(NewVendor().AssociatedLedgerAccountIds);
    }

    [Fact]
    public void New_Vendor_Stores_Associated_Ledger_Accounts()
    {
        var a1 = Guid.NewGuid();
        var v = new VendorEntity(
            personType: PersonType.Company,
            name: "X", tradeName: null, document: "11222333000181",
            email: null, phone: null, address: Address.Empty, paymentTerms: null,
            bankInfo: BankInfo.Empty, taxRegime: TaxRegime.Unknown, paymentSchedule: PaymentSchedule.Default,
            associatedLedgerAccountIds: new[] { a1 });
        Assert.Equal(new[] { a1 }, v.AssociatedLedgerAccountIds);
    }

    [Fact]
    public void Update_Replaces_Associated_Ledger_Accounts()
    {
        var v = NewVendor();
        var a1 = Guid.NewGuid();
        var a2 = Guid.NewGuid();
        v.Update(PersonType.Company, "Gráfica Sul Ltda", "Gráfica Sul", "11222333000181",
            "contato@gs.com", "5133334444", Address.Empty, "30 dias", BankInfo.Empty, TaxRegime.SimplesNacional,
            PaymentSchedule.Default, associatedLedgerAccountIds: new[] { a1, a2 });
        Assert.Equal(new[] { a1, a2 }, v.AssociatedLedgerAccountIds);
    }
}
