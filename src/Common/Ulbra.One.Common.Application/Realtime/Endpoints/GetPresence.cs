using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Common.Application.Realtime.Endpoints;

/// <summary>Quem está presente num recurso. Devolve vazio (nunca erro) quando o chamador não pode ver o
/// recurso, OU quando o backplane está fora — presença é acessório e some sem quebrar a tela (§7.4 do
/// design). Um escopo DESCONHECIDO é diferente: isso não é "sem acesso", é um erro de chamada (rota
/// errada, typo, escopo que perdeu o authorizer) — recusa com o mesmo 400 que POST /presence usa, em vez
/// de devolver uma lista vazia indistinguível de "ninguém está presente".</summary>
public sealed class GetPresence : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/presence", Handle).WithName("GetPresence").WithTags("Realtime")
            .WithSummary("Lista quem está vendo um recurso agora.")
            .WithDescription("Ids dos usuários presentes. Vazio quando ninguém está, quando o chamador "
                + "não pode ver o recurso, ou quando o backplane de realtime está indisponível.")
            .RequireAuthorization()
            .Produces<IReadOnlyCollection<Guid>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(
        [SwaggerSchema("Escopo do recurso, ex.: \"requisition\". Precisa ter um IPresenceAuthorizer "
            + "registrado — um escopo desconhecido é recusado (fail-closed).")]
        string scope,
        [SwaggerSchema("Id do recurso dentro do escopo.")]
        Guid resourceId,
        [FromServices] IEnumerable<IPresenceAuthorizer> authorizers,
        IRealtimeBackplane backplane,
        CancellationToken ct)
    {
        var authorizer = authorizers.FirstOrDefault(a => a.Scope == scope);
        if (authorizer is null)
            return ErrorResults.ValidationError(
                new Dictionary<string, string[]> { ["scope"] = ["Escopo de presença desconhecido."] },
                ErrorCodes.Presence.UnknownScope);

        if (!await authorizer.CanViewAsync(resourceId, ct))
            return TypedResults.Ok<IReadOnlyCollection<Guid>>([]);

        return TypedResults.Ok(await backplane.ReadPresenceAsync(scope, resourceId, ct));
    }
}
