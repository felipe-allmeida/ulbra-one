using Ulbra.One.Registry.Domain.ValueObjects;

namespace Ulbra.One.Registry.Application.Validation;

internal static class AddressMapping
{
    public static Address ToAddress(this AddressRequest? req) => req is null
        ? Address.Empty
        : new Address(
            req.Cep ?? "",
            req.Street ?? "",
            req.Number ?? "",
            req.Complement,
            req.Neighborhood ?? "",
            req.City ?? "",
            req.State ?? "");
}
