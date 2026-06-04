using Ulbra.One.Registry.Domain.ValueObjects;

namespace Ulbra.One.UnitTests.Registry;

public class AddressTests
{
    [Fact]
    public void Empty_Has_All_Blank_Fields()
    {
        var a = Address.Empty;
        Assert.Equal("", a.Cep);
        Assert.Equal("", a.City);
    }

    [Fact]
    public void Records_Are_Value_Equal()
    {
        var a = new Address("01001000", "Praça da Sé", "1", null, "Sé", "São Paulo", "SP");
        var b = new Address("01001000", "Praça da Sé", "1", null, "Sé", "São Paulo", "SP");
        Assert.Equal(a, b);
    }
}
