using Ulbra.One.Registry.Domain.Entities;
using Ulbra.One.Registry.Domain.ValueObjects;

namespace Ulbra.One.UnitTests.Registry;

public class EmployeeEntityTests
{
    [Fact]
    public void New_Employee_Normalizes_Cpf_And_Is_Active()
    {
        var e = new EmployeeEntity(Guid.NewGuid(), "Maria Souza", "529.982.247-25",
            "maria@ulbra.br", "5133221100", "Analista", Guid.NewGuid(), null,
            new DateOnly(2026, 1, 15), Address.Empty);
        Assert.True(e.Active);
        Assert.Equal("52998224725", e.Document);
        Assert.Equal("Analista", e.Role);
        Assert.Null(e.DeletedAt);
    }

    [Fact]
    public void Update_Changes_Role_And_CostCenter()
    {
        var unit = Guid.NewGuid();
        var e = new EmployeeEntity(Guid.NewGuid(), "Maria", "52998224725", null, null,
            "Analista", unit, null, new DateOnly(2026, 1, 15), Address.Empty);
        var cc = Guid.NewGuid();
        e.Update("Maria Souza", "52998224725", null, null, "Coordenadora", unit, cc, new DateOnly(2026, 1, 15), Address.Empty);
        Assert.Equal("Coordenadora", e.Role);
        Assert.Equal(cc, e.CostCenterId);
    }

    [Fact]
    public void MarkDeleted_Sets_DeletedAt()
    {
        var e = new EmployeeEntity(Guid.NewGuid(), "Maria", "52998224725", null, null,
            "Analista", Guid.NewGuid(), null, new DateOnly(2026, 1, 15), Address.Empty);
        var at = new DateTimeOffset(2026, 6, 4, 0, 0, 0, TimeSpan.Zero);
        e.MarkDeleted(at);
        Assert.Equal(at, e.DeletedAt);
    }
}
