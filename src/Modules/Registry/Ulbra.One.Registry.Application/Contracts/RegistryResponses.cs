using Ulbra.One.Registry.Domain.Entities;
using Ulbra.One.Registry.Domain.ValueObjects;

namespace Ulbra.One.Registry.Application.Contracts;

/// <summary>Address response shape. Matches the frontend <c>Address</c> type.</summary>
public sealed record AddressResponse(
    string Cep,
    string Street,
    string Number,
    string? Complement,
    string Neighborhood,
    string City,
    string State)
{
    public static AddressResponse From(Address a) =>
        new(a.Cep, a.Street, a.Number, a.Complement, a.Neighborhood, a.City, a.State);
}

/// <summary>Vendor response shape. Matches the frontend <c>Vendor</c> type.
/// <c>PersonType</c> serializes as an enum string via the global converter (current behavior).</summary>
public sealed record VendorResponse(
    Guid Id,
    Guid CompanyId,
    PersonType PersonType,
    string Name,
    string? TradeName,
    string Document,
    string? Email,
    string? Phone,
    AddressResponse? Address,
    string? PaymentTerms,
    string? BankInfo,
    bool Active)
{
    public static VendorResponse From(VendorEntity v) => new(
        v.Id, v.CompanyId, v.PersonType, v.Name, v.TradeName, v.Document, v.Email, v.Phone,
        AddressResponse.From(v.Address), v.PaymentTerms, v.BankInfo, v.Active);
}

/// <summary>Customer response shape. Matches the frontend <c>Customer</c> type.</summary>
public sealed record CustomerResponse(
    Guid Id,
    Guid CompanyId,
    PersonType PersonType,
    string Name,
    string? TradeName,
    string Document,
    string? Email,
    string? Phone,
    AddressResponse? Address,
    decimal? CreditLimit,
    bool Active)
{
    public static CustomerResponse From(CustomerEntity c) => new(
        c.Id, c.CompanyId, c.PersonType, c.Name, c.TradeName, c.Document, c.Email, c.Phone,
        AddressResponse.From(c.Address), c.CreditLimit, c.Active);
}

/// <summary>Employee response shape. Matches the frontend <c>Employee</c> type.</summary>
public sealed record EmployeeResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string Document,
    string? Email,
    string? Phone,
    string Role,
    Guid UnitId,
    Guid? CostCenterId,
    DateOnly HireDate,
    AddressResponse? Address,
    bool Active)
{
    public static EmployeeResponse From(EmployeeEntity e) => new(
        e.Id, e.CompanyId, e.Name, e.Document, e.Email, e.Phone, e.Role, e.UnitId, e.CostCenterId,
        e.HireDate, AddressResponse.From(e.Address), e.Active);
}
