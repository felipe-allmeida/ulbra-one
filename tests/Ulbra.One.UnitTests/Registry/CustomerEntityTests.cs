using Ulbra.One.Registry.Domain.Entities;
using Ulbra.One.Registry.Domain.ValueObjects;

namespace Ulbra.One.UnitTests.Registry;

public class CustomerEntityTests
{
    [Fact]
    public void New_Customer_Normalizes_Document_And_Is_Active()
    {
        var c = new CustomerEntity(Guid.NewGuid(), PersonType.Company, "Cliente X SA", "Cliente X",
            "11.222.333/0001-81", "x@x.com", "5111112222", Address.Empty, creditLimit: 5000m);
        Assert.True(c.Active);
        Assert.Equal("11222333000181", c.Document);
        Assert.Equal(5000m, c.CreditLimit);
        Assert.Null(c.DeletedAt);
    }

    [Fact]
    public void Update_Changes_CreditLimit()
    {
        var c = new CustomerEntity(Guid.NewGuid(), PersonType.Individual, "João", null,
            "52998224725", null, null, Address.Empty, null);
        c.Update(PersonType.Individual, "João Silva", null, "52998224725", null, null, Address.Empty, 1000m);
        Assert.Equal("João Silva", c.Name);
        Assert.Equal(1000m, c.CreditLimit);
    }

    [Fact]
    public void MarkDeleted_Sets_DeletedAt()
    {
        var c = new CustomerEntity(Guid.NewGuid(), PersonType.Individual, "João", null,
            "52998224725", null, null, Address.Empty, null);
        var at = new DateTimeOffset(2026, 6, 4, 0, 0, 0, TimeSpan.Zero);
        c.MarkDeleted(at);
        Assert.Equal(at, c.DeletedAt);
    }
}
