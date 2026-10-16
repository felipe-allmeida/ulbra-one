using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Authentication;

namespace Ulbra.One.Common.Application.Realtime.Endpoints;

/// <summary>Sai da presença. Best-effort no cliente — o unload chama
/// <c>fetch('/api/realtime/presence?...', { method: 'DELETE', keepalive: true })</c>, NUNCA
/// <c>navigator.sendBeacon</c>: sendBeacon só manda POST, não tem verbo DELETE (Task 8 implementa isso).
/// Se a chamada não chegar de jeito nenhum, o TTL resolve em 45 s. A remoção em si não autoriza contra o
/// recurso — sair é sempre seguro e idempotente, exigir leitura aqui só criaria um caminho de falha à
/// toa — mas o SINAL de "presença mudou" que ela dispara é diferente: publicar exige o mesmo authorizer
/// do enter, porque qualquer chamador autenticado sem essa checagem poderia forçar um refetch em quem
/// está vendo um recurso que ele nem pode abrir (ver revisão da Task 7).</summary>
public sealed class LeavePresence : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapDelete("/presence", Handle).WithName("LeavePresence").WithTags("Realtime")
            .WithSummary("Sai da presença de um recurso.")
            .WithDescription("Remove o chamador da presença do recurso. Idempotente, e não autoriza "
                + "contra o recurso — sair é sempre seguro.")
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent);

    private static async Task<IResult> Handle(
        [SwaggerSchema("Id da conexão SSE que deixa de estar presente.")]
        Guid connectionId,
        [SwaggerSchema("Escopo do recurso, ex.: \"requisition\".")]
        string scope,
        [SwaggerSchema("Id do recurso dentro do escopo.")]
        Guid resourceId,
        UserContext userContext,
        [FromServices] IEnumerable<IPresenceAuthorizer> authorizers,
        IRealtimeBackplane backplane,
        CancellationToken ct)
    {
        var userId = Guid.Parse(userContext.UserId);

        await backplane.LeavePresenceAsync(scope, resourceId, userId, ct);
        // Mesmo raciocínio do enter: a conexão pode estar em outra réplica, então a remoção local viaja
        // pelo backplane e só a réplica dona aplica.
        await backplane.PublishAsync(new RealtimeEnvelope(
            RealtimeTarget.ForConnection(connectionId, userId, scope, resourceId),
            new RealtimeSignal(PresenceCommandKind.Leave, null)), ct);

        // Só publica o sinal "presença mudou" quando o chamador poderia ver o recurso — sem isso,
        // qualquer usuário autenticado força um refetch em quem está vendo um recurso arbitrário,
        // sabendo só o (scope, resourceId). A remoção acima já aconteceu de qualquer forma.
        var authorizer = authorizers.FirstOrDefault(a => a.Scope == scope);
        if (authorizer is not null && await authorizer.CanViewAsync(resourceId, ct))
            await EnterPresence.NotifyChanged(backplane, scope, resourceId, ct);

        return TypedResults.NoContent();
    }
}
