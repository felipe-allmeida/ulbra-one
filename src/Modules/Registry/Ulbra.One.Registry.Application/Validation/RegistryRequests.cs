using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Infrastructure.Normalization;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Registry.Application.Validation;

public sealed record AddressRequest(
    string? Cep, string? Street, string? Number, string? Complement,
    string? Neighborhood, string? City, string? State);

public sealed record BankInfoRequest(
    string? BankCode, string? Branch, string? Account, string? AccountDigit, string? PixKey);

public sealed record VendorRequest(
    CompanyId CompanyId, PersonType PersonType, [property: NormalizeText] string Name,
    [property: NormalizeText] string? TradeName, [property: NormalizeText] string Document,
    string? Email, string? Phone, AddressRequest? Address,
    [property: NormalizeText] string? PaymentTerms, BankInfoRequest? BankInfo, TaxRegime TaxRegime);

public sealed record CustomerRequest(
    CompanyId CompanyId, PersonType PersonType, [property: NormalizeText] string Name,
    [property: NormalizeText] string? TradeName, [property: NormalizeText] string Document,
    string? Email, string? Phone, AddressRequest? Address, decimal? CreditLimit);

public sealed record EmployeeRequest(
    CompanyId CompanyId, [property: NormalizeText] string Name, [property: NormalizeText] string Document,
    string? Email, string? Phone, [property: NormalizeText] string Role,
    UnitId UnitId, CostCenterId? CostCenterId, DateOnly HireDate, AddressRequest? Address);
