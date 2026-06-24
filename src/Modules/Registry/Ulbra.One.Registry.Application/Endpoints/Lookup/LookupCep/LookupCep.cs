using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ulbra.One.Common.Application;
using Ulbra.One.Registry.Application.Lookups;

namespace Ulbra.One.Registry.Application.Endpoints.Lookup.LookupCep;

public sealed class LookupCep : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("/cep/{cep}", Handle)
            .WithName("LookupCep")
            .WithTags("Registry")
            .RequireAuthorization()
            .Produces<CepResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(string cep, ICepLookup lookup, CancellationToken ct)
    {
        var result = await lookup.FindAsync(cep, ct);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
    }
}
