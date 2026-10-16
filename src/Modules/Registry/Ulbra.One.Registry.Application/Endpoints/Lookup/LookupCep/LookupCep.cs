using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Registry.Application.Lookups;

namespace Ulbra.One.Registry.Application.Endpoints.Lookup.LookupCep;

public sealed class LookupCep : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("/cep/{cep}", Handle)
            .WithName("LookupCep")
            .WithTags("Registry")
            .WithSummary("Looks up an address by postal code.")
            .WithDescription("Resolves a Brazilian CEP into street, neighborhood, city and state, so the "
                + "address fields of a registration can be prefilled. A CEP that does not exist returns 404 "
                + "— fill the address in by hand.")
            .RequirePermission(Permissions.RegistryRead)
            .Produces<CepResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        [SwaggerSchema("Postal code to look up. Digits only or formatted — both are accepted.")]
        [SwaggerExample("90420-111")]
        string cep,
        ICepLookup lookup, CancellationToken ct)
    {
        var result = await lookup.FindAsync(cep, ct);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
    }
}
