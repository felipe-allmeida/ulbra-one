using Ulbra.One.Registry.Domain.Entities;
using Ulbra.One.Registry.Domain.ValueObjects;

namespace Ulbra.One.UnitTests.Registry;

public class VendorEntityTests
{
    private static VendorEntity NewVendor() => new(
        companyId: Guid.NewGuid(), personType: PersonType.Company,
        name: "Gráfica Sul Ltda", tradeName: "Gráfica Sul", document: "11.222.333/0001-81",
        email: "contato@gs.com", phone: "5133334444", address: Address.Empty,
        paymentTerms: "30 dias", bankInfo: "Banco 001 / Ag 1234");

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
            "novo@x.com", "5199998888", Address.Empty, "à vista", null);
        Assert.Equal("Novo Nome", v.Name);
        Assert.Equal("à vista", v.PaymentTerms);
        Assert.Null(v.BankInfo);
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
}
