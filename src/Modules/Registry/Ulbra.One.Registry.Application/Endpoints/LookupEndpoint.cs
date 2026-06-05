using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ulbra.One.Common.Application;
using Ulbra.One.Registry.Infrastructure.Lookups;

namespace Ulbra.One.Registry.Application.Endpoints;

public sealed class LookupEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/lookup").WithTags("Registry").RequireAuthorization();

        group.MapGet("/cep/{cep}", async (string cep, ICepLookup lookup, CancellationToken ct) =>
        {
            var result = await lookup.FindAsync(cep, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).WithName("LookupCep")
            .Produces<CepResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/cnpj/{cnpj}", async (string cnpj, ICnpjLookup lookup, CancellationToken ct) =>
        {
            var result = await lookup.FindAsync(cnpj, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).WithName("LookupCnpj")
            .Produces<CnpjResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
    }
}
