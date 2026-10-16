using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Common.Application.Realtime.Endpoints;

/// <summary>Sai da presença. Best-effort no cliente — o unload chama
/// <c>fetch('/api/realtime/presence?...', { method: 'DELETE', keepalive: true })</c>, NUNCA
/// <c>navigator.sendBeacon</c>: sendBeacon só manda POST, não tem verbo DELETE (Task 8 implementa isso).
/// O cliente retenta com backoff enquanto a aba viver (ver <c>leaveWithRetry</c> em usePresence.ts), porque o
/// TTL NÃO cobre um leave perdido: ele só resolve quando a CONEXÃO SSE morre — enquanto ela vive, a entrada
/// local no registry sobrevive e a renovação replica-level recria o membro no Redis a cada heartbeat.
/// Fechar a aba mata o stream junto, e aí sim os 45 s do TTL resolvem. A remoção em si não autoriza contra o
/// recurso — sair é sempre seguro e idempotente, exigir leitura aqui só criaria um caminho de falha à
/// toa — mas o SINAL de "presença mudou" que ela dispara é diferente: publicar exige o mesmo authorizer
/// do enter, porque qualquer chamador autenticado sem essa checagem poderia forçar um refetch em quem
/// está vendo um recurso que ele nem pode abrir (ver revisão da Task 7).
/// <para>
/// Os quatro parâmetros são <c>Guid?</c>/<c>string?</c>/<c>long?</c> — nunca os tipos não anuláveis que
/// pareceriam naturais para query obrigatória. Um parâmetro de query NÃO anulável, quando o cliente
/// simplesmente OMITE a chave, faz o model binder do ASP.NET lançar BadHttpRequestException antes do
/// handler rodar; UseAppExceptionHandler só trata DbUpdateConcurrencyException, então essa exceção vira
/// 500 general.unexpected — pior que o 400 que estamos tentando evitar. Tornar os quatro anuláveis e
/// validar explicitamente é o que transforma "campo ausente" em erro de validação tipado, para os quatro
/// igualmente: um 500 num unload de aba (onde depurar já é difícil) por causa de um cliente mal formado
/// nunca é aceitável.</para></summary>
public sealed class LeavePresence : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapDelete("/presence", Handle).WithName("LeavePresence").WithTags("Realtime")
            .WithSummary("Sai da presença de um recurso.")
            .WithDescription("Remove o chamador da presença do recurso. Idempotente, e não autoriza "
                + "contra o recurso — sair é sempre seguro.")
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(
        [SwaggerSchema("Id da conexão SSE que deixa de estar presente.")]
        Guid? connectionId,
        [SwaggerSchema("Escopo do recurso, ex.: \"requisition\".")]
        string? scope,
        [SwaggerSchema("Id do recurso dentro do escopo.")]
        Guid? resourceId,
        [SwaggerSchema("Contador monotônico da aba: cresce a cada chamada de entrar/sair de presença "
            + "que ela faz, em qualquer recurso. Desempata a ordem entre este leave e um enter "
            + "concorrente da mesma conexão quando os dois publishes chegam fora de ordem entre "
            + "réplicas — um comando com seq menor ou igual ao último já aplicado é descartado.")]
        [SwaggerExample("2")]
        long? seq,
        UserContext userContext,
        IEnumerable<IPresenceAuthorizer> authorizers,
        IRealtimeBackplane backplane,
        CancellationToken ct)
    {
        if (connectionId is null)
            return ErrorResults.ValidationError(
                new Dictionary<string, string[]> { ["connectionId"] = ["Id de conexão inválido."] },
                ErrorCodes.Presence.InvalidConnectionId);

        if (string.IsNullOrEmpty(scope))
            return ErrorResults.ValidationError(
                new Dictionary<string, string[]> { ["scope"] = ["Escopo de presença desconhecido."] },
                ErrorCodes.Presence.UnknownScope);

        if (resourceId is null)
            return ErrorResults.ValidationError(
                new Dictionary<string, string[]> { ["resourceId"] = ["Id de recurso inválido."] },
                ErrorCodes.Presence.InvalidResourceId);

        // seq <= 0 (campo omitido, ou explicitamente 0/negativo) faria TryAdvanceSeq descartar este
        // leave em silêncio se um enter com seq=0 já tivesse aplicado — a entrada local sobrevive e a
        // renovação seguinte (capaz de recriar) a mantém viva pelo resto da vida do stream inteiro.
        if (seq is null || seq <= 0)
            return ErrorResults.ValidationError(
                new Dictionary<string, string[]> { ["seq"] = ["Número de sequência inválido."] },
                ErrorCodes.Presence.InvalidSeq);

        var userId = Guid.Parse(userContext.UserId);

        await backplane.LeavePresenceAsync(scope, resourceId.Value, userId, ct);
        // Mesmo raciocínio do enter: a conexão pode estar em outra réplica, então a remoção local viaja
        // pelo backplane e só a réplica dona aplica — e só se `seq` for maior que o último comando
        // aplicado para esta conexão (ver EnterPresence.Handle e RealtimeConnectionRegistry).
        await backplane.PublishAsync(new RealtimeEnvelope(
            RealtimeTarget.ForConnection(connectionId.Value, userId, scope, resourceId.Value, seq.Value),
            new RealtimeSignal(PresenceCommandKind.Leave, null)), ct);

        // Só publica o sinal "presença mudou" quando o chamador poderia ver o recurso — sem isso,
        // qualquer usuário autenticado força um refetch em quem está vendo um recurso arbitrário,
        // sabendo só o (scope, resourceId). A remoção acima já aconteceu de qualquer forma.
        var authorizer = authorizers.FirstOrDefault(a => a.Scope == scope);
        if (authorizer is not null && await authorizer.CanViewAsync(resourceId.Value, ct))
            await EnterPresence.NotifyChanged(backplane, scope, resourceId.Value, ct);

        return TypedResults.NoContent();
    }
}
