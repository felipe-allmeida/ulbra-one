namespace Ulbra.One.Registry.Domain.ValueObjects;

/// <summary>Endereço como value object (EF owned type). Imutável.</summary>
public sealed record Address(
    string Cep,
    string Street,
    string Number,
    string? Complement,
    string Neighborhood,
    string City,
    string State)
{
    public static Address Empty => new("", "", "", null, "", "", "");
}
