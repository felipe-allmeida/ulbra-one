using Ulbra.One.Registry.Domain.Entities;
using Ulbra.One.Registry.Domain.ValueObjects;

namespace Ulbra.One.UnitTests.Registry;

public class BankInfoTests
{
    [Fact]
    public void Empty_is_all_blank()
    {
        var e = BankInfo.Empty;
        Assert.Equal("", e.BankCode);
        Assert.Equal("", e.PixKey);
    }

    [Fact]
    public void Vendor_apply_sets_bank_info_and_regime()
    {
        var v = new VendorEntity(Guid.NewGuid(), PersonType.Company, "Acme", null, "12345678000199",
            null, null, Ulbra.One.Registry.Domain.ValueObjects.Address.Empty, null,
            new BankInfo("033", "1234", "56789", "0", "acme@pix.com"), TaxRegime.SimplesNacional);

        Assert.Equal("033", v.BankInfo.BankCode);
        Assert.Equal(TaxRegime.SimplesNacional, v.TaxRegime);
    }
}
