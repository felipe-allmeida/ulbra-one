using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ulbra.One.Common.Application;
using Ulbra.One.Registry.Application.Lookups;

namespace Ulbra.One.Registry.Application.Endpoints.Lookup.LookupCnpj;

public sealed class LookupCnpj : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("/cnpj/{cnpj}", Handle)
            .WithName("LookupCnpj")
            .WithTags("Registry")
            .RequireAuthorization()
            .Produces<CnpjResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(string cnpj, ICnpjLookup lookup, CancellationToken ct)
    {
        var result = await lookup.FindAsync(cnpj, ct);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
    }
}
