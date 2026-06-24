namespace Ulbra.One.Registry.Application.Lookups;

public sealed record CepResult(string Cep, string Street, string Neighborhood, string City, string State);

public sealed record CnpjAddress(string Cep, string Street, string Number, string Neighborhood, string City, string State);
public sealed record CnpjResult(string RazaoSocial, string? NomeFantasia, CnpjAddress Address);
