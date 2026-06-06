using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Registry.Application.Validation;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.UnitTests.Registry;

public class RequestValidatorTests
{
    [Fact]
    public void Vendor_Company_Requires_Valid_Cnpj()
    {
        var v = new VendorRequestValidator();
        var bad = new VendorRequest(new CompanyId(Guid.NewGuid()), PersonType.Company, "X", null, "11222333000100", null, null, null, null, null);
        Assert.False(v.Validate(bad).IsValid);

        var ok = new VendorRequest(new CompanyId(Guid.NewGuid()), PersonType.Company, "X", null, "11222333000181", null, null, null, null, null);
        Assert.True(v.Validate(ok).IsValid);
    }

    [Fact]
    public void Vendor_Individual_Requires_Valid_Cpf()
    {
        var v = new VendorRequestValidator();
        var ok = new VendorRequest(new CompanyId(Guid.NewGuid()), PersonType.Individual, "X", null, "52998224725", null, null, null, null, null);
        Assert.True(v.Validate(ok).IsValid);
    }

    [Fact]
    public void Vendor_Empty_Name_Is_Invalid()
    {
        var v = new VendorRequestValidator();
        var bad = new VendorRequest(new CompanyId(Guid.NewGuid()), PersonType.Individual, "", null, "52998224725", null, null, null, null, null);
        Assert.False(v.Validate(bad).IsValid);
    }

    [Fact]
    public void Vendor_Bad_Email_Is_Invalid()
    {
        var v = new VendorRequestValidator();
        var bad = new VendorRequest(new CompanyId(Guid.NewGuid()), PersonType.Individual, "X", null, "52998224725", "not-an-email", null, null, null, null);
        Assert.False(v.Validate(bad).IsValid);
    }

    [Fact]
    public void Vendor_Empty_CompanyId_Is_Invalid()
    {
        var v = new VendorRequestValidator();
        var bad = new VendorRequest(new CompanyId(Guid.Empty), PersonType.Individual, "X", null, "52998224725", null, null, null, null, null);
        Assert.False(v.Validate(bad).IsValid);
    }

    [Fact]
    public void Employee_Requires_Valid_Cpf_And_Unit()
    {
        var v = new EmployeeRequestValidator();
        var bad = new EmployeeRequest(new CompanyId(Guid.NewGuid()), "Maria", "12345678900", null, null, "Analista", new UnitId(Guid.Empty), null, new DateOnly(2026, 1, 1), null);
        Assert.False(v.Validate(bad).IsValid);

        var ok = new EmployeeRequest(new CompanyId(Guid.NewGuid()), "Maria", "52998224725", null, null, "Analista", new UnitId(Guid.NewGuid()), null, new DateOnly(2026, 1, 1), null);
        Assert.True(v.Validate(ok).IsValid);
    }

    [Fact]
    public void Customer_Company_Requires_Valid_Cnpj()
    {
        var v = new CustomerRequestValidator();
        var ok = new CustomerRequest(new CompanyId(Guid.NewGuid()), PersonType.Company, "X", null, "11222333000181", null, null, null, 1000m);
        Assert.True(v.Validate(ok).IsValid);
    }
}
