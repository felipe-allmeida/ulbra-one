using System.Linq;
using Ulbra.One.Registry.Domain.ValueObjects;

namespace Ulbra.One.Registry.Application.Validation;

internal static class AddressMapping
{
    /// <summary>CEP stored normalized (digits only) to match the 8-char column — mirrors document handling.</summary>
    public static string NormalizeCep(string? cep) => new((cep ?? "").Where(char.IsDigit).ToArray());

    public static Address ToAddress(this AddressRequest? req) => req is null
        ? Address.Empty
        : new Address(
            NormalizeCep(req.Cep),
            req.Street ?? "",
            req.Number ?? "",
            req.Complement,
            req.Neighborhood ?? "",
            req.City ?? "",
            req.State ?? "");
}
