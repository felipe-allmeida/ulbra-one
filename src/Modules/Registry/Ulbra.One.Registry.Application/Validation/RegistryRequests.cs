using Ulbra.One.Common.Infrastructure.Normalization;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Registry.Application.Validation;

public sealed record AddressRequest(
    string? Cep, string? Street, string? Number, string? Complement,
    string? Neighborhood, string? City, string? State);

public sealed record VendorRequest(
    Guid CompanyId, PersonType PersonType, [property: NormalizeText] string Name,
    [property: NormalizeText] string? TradeName, [property: NormalizeText] string Document,
    string? Email, string? Phone, AddressRequest? Address,
    [property: NormalizeText] string? PaymentTerms, string? BankInfo);

public sealed record CustomerRequest(
    Guid CompanyId, PersonType PersonType, [property: NormalizeText] string Name,
    [property: NormalizeText] string? TradeName, [property: NormalizeText] string Document,
    string? Email, string? Phone, AddressRequest? Address, decimal? CreditLimit);

public sealed record EmployeeRequest(
    Guid CompanyId, [property: NormalizeText] string Name, [property: NormalizeText] string Document,
    string? Email, string? Phone, [property: NormalizeText] string Role,
    Guid UnitId, Guid? CostCenterId, DateOnly HireDate, AddressRequest? Address);
