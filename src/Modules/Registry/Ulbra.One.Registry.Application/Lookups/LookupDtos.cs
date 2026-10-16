using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Registry.Domain.Entities;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Registry.Application.Lookups;

[SwaggerSchema("Address found for a CEP, to pre-fill an address form. It carries no street number — a "
    + "CEP identifies the street, not the building.")]
public sealed record CepResult(
    [property: SwaggerSchema("The CEP that was looked up, digits only.")]
    [property: SwaggerExample("92425900")]
    string Cep,
    [property: SwaggerSchema("Street name.")]
    [property: SwaggerExample("Avenida Farroupilha")]
    string Street,
    [property: SwaggerSchema("Neighborhood.")]
    [property: SwaggerExample("São José")]
    string Neighborhood,
    [property: SwaggerSchema("City name.")]
    [property: SwaggerExample("Canoas")]
    string City,
    [property: SwaggerSchema("State, as the two-letter UF.")]
    [property: SwaggerExample("RS")]
    string State);

[SwaggerSchema("Registered address of a company from the CNPJ lookup.")]
public sealed record CnpjAddress(
    [property: SwaggerSchema("CEP, digits only.")]
    [property: SwaggerExample("92425900")]
    string Cep,
    [property: SwaggerSchema("Street name.")]
    [property: SwaggerExample("Avenida Farroupilha")]
    string Street,
    [property: SwaggerSchema("Street number, as registered.")]
    [property: SwaggerExample("8001")]
    string Number,
    [property: SwaggerSchema("Neighborhood.")]
    [property: SwaggerExample("São José")]
    string Neighborhood,
    [property: SwaggerSchema("City name.")]
    [property: SwaggerExample("Canoas")]
    string City,
    [property: SwaggerSchema("State, as the two-letter UF.")]
    [property: SwaggerExample("RS")]
    string State);

/// <summary>Company data from the CNPJ lookup. `SuggestedTaxRegime`/`PrimaryCnae*` are best-effort hints
/// from BrasilAPI (may be null when the registry data is incomplete) — the form fills them only as defaults.</summary>
[SwaggerSchema("Company data from the CNPJ lookup, to pre-fill a vendor form. Everything here is a "
    + "DEFAULT the operator can override — it comes from an upstream registry, not from us.")]
public sealed record CnpjResult(
    [property: SwaggerSchema("Registered legal name.")]
    [property: SwaggerExample("Construtora Horizonte Ltda")]
    string RazaoSocial,
    [property: SwaggerSchema("Trade name, when the registry has one.")]
    [property: SwaggerExample("Horizonte Engenharia")]
    string? NomeFantasia,
    [property: SwaggerSchema("Registered address.")]
    CnpjAddress Address,
    [property: SwaggerSchema("Tax regime inferred from the registry data. Best-effort — null when it "
        + "could not be inferred, which is not the same as the company having no regime.")]
    TaxRegime? SuggestedTaxRegime = null,
    [property: SwaggerSchema("Primary CNAE code. It is what suggests the service nature and the "
        + "withholding profile downstream.")]
    [property: SwaggerExample("4120400")]
    string? PrimaryCnaeCode = null,
    [property: SwaggerSchema("Description of the primary CNAE.")]
    [property: SwaggerExample("Construção de edifícios")]
    string? PrimaryCnaeDescription = null);

/// <summary>CNPJ lookup outcome — tells a real 404 apart from an upstream failure (403/429/5xx/timeout).</summary>
public enum CnpjLookupStatus { Found, NotFound, Unavailable }

public sealed record CnpjLookupOutcome(CnpjLookupStatus Status, CnpjResult? Value)
{
    public static readonly CnpjLookupOutcome NotFound = new(CnpjLookupStatus.NotFound, null);
    public static readonly CnpjLookupOutcome Unavailable = new(CnpjLookupStatus.Unavailable, null);
    public static CnpjLookupOutcome Found(CnpjResult value) => new(CnpjLookupStatus.Found, value);
}
