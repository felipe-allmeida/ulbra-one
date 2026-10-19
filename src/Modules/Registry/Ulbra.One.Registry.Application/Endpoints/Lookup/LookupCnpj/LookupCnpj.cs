using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Registry.Application.Lookups;

namespace Ulbra.One.Registry.Application.Endpoints.Lookup.LookupCnpj;

public sealed class LookupCnpj : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("/cnpj/{cnpj}", Handle)
            .WithName("LookupCnpj")
            .WithTags("Registry")
            .WithSummary("Looks up a company by CNPJ.")
            .WithDescription("Resolves a CNPJ into legal name, trading name, address and primary CNAE, so a "
                + "vendor or customer registration can be prefilled. Three outcomes worth handling "
                + "separately: 200 with the data, 404 when the CNPJ does not exist, and 503 when the "
                + "external lookup is down — the last one is not a bad request, it means fall back to "
                + "filling the form by hand.")
            .RequirePermission(Permissions.RegistryRead)
            .Produces<CnpjResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<IResult> Handle(
        [SwaggerSchema("CNPJ to look up. Digits only or formatted — both are accepted.")]
        [SwaggerExample("12.345.678/0001-95")]
        string cnpj,
        ICnpjLookup lookup, CancellationToken ct)
    {
        var outcome = await lookup.FindAsync(cnpj, ct);
        return outcome.Status switch
        {
            CnpjLookupStatus.Found => TypedResults.Ok(outcome.Value),
            CnpjLookupStatus.NotFound => TypedResults.NotFound(),
            _ => ErrorResults.ServiceUnavailable("A consulta de CNPJ está indisponível no momento. Preencha os dados manualmente.", ErrorCodes.Cnpj.LookupUnavailable),
        };
    }
}
