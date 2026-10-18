using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Common.Application.Realtime.Endpoints;

public sealed record PresenceRequest(
    [property: SwaggerSchema("Id da conexão SSE (recebido no evento \"connected\" de GET /realtime/stream) "
        + "que deve ser marcada como presente.")]
    string ConnectionId,
    [property: SwaggerSchema("Escopo do recurso, ex.: \"requisition\". Precisa ter um IPresenceAuthorizer "
        + "registrado — um escopo desconhecido é recusado (fail-closed).")]
    string Scope,
    [property: SwaggerSchema("Id do recurso dentro do escopo.")]
    Guid ResourceId,
    [property: SwaggerSchema("Contador monotônico da aba: cresce a cada chamada de entrar/sair de "
        + "presença que ela faz, em qualquer recurso. Desempata a ordem entre este enter e um leave "
        + "concorrente da mesma conexão quando os dois publishes chegam fora de ordem entre réplicas — "
        + "um comando com seq menor ou igual ao último já aplicado é descartado.")]
    [property: SwaggerExample("1")]
    long Seq);

/// <summary>Entra na presença de um recurso. Não há heartbeat do cliente: a réplica renova o TTL no
/// heartbeat SSE que já manda (§6.2 do design). O cliente só chama isto ao abrir a tela.</summary>
public sealed class EnterPresence : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/presence", Handle).WithName("EnterPresence").WithTags("Realtime")
            .WithSummary("Entra na presença de um recurso.")
            .WithDescription("Registra o chamador como presente no recurso indicado, para que os demais "
                + "vejam. Autoriza contra o próprio recurso: só entra quem poderia abri-lo.")
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        PresenceRequest req,
        UserContext userContext,
        IEnumerable<IPresenceAuthorizer> authorizers,
        IRealtimeBackplane backplane,
        CancellationToken ct)
    {
        var authorizer = authorizers.FirstOrDefault(a => a.Scope == req.Scope);
        if (authorizer is null)
            return ErrorResults.ValidationError(
                new Dictionary<string, string[]> { ["scope"] = ["Escopo de presença desconhecido."] },
                ErrorCodes.Presence.UnknownScope);

        // 404-hide, como GetRequisition: não confirmamos existência para quem não pode ver.
        if (!await authorizer.CanViewAsync(req.ResourceId, ct)) return TypedResults.NotFound();

        if (!Guid.TryParse(req.ConnectionId, out var connectionId))
            return ErrorResults.ValidationError(
                new Dictionary<string, string[]> { ["connectionId"] = ["Id de conexão inválido."] },
                ErrorCodes.Presence.InvalidConnectionId);

        // Seq é um long simples no corpo, sem [Required]: um cliente que omite o campo, reenvia o
        // mesmo valor, ou zera o contador por recurso, bina em 0 sem erro nenhum — e 0 é indistinguível
        // de "nunca aplicado" só até o primeiro comando; depois disso um leave com seq=0 é descartado
        // por TryAdvanceSeq (0 <= 0), a entrada local sobrevive, e a renovação seguinte a recria: uma
        // presença fantasma pelo resto da vida do stream. Recusar seq <= 0 aqui fecha isso na borda.
        if (req.Seq <= 0)
            return ErrorResults.ValidationError(
                new Dictionary<string, string[]> { ["seq"] = ["Número de sequência inválido."] },
                ErrorCodes.Presence.InvalidSeq);

        var userId = Guid.Parse(userContext.UserId);

        await backplane.EnterPresenceAsync(req.Scope, req.ResourceId, userId, ct);
        // A conexão SSE do chamador pode estar em outra réplica (sem afinidade de sessão, ver revisão da
        // Task 7) — não dá para mutar RealtimeConnectionRegistry direto aqui. O comando viaja pelo mesmo
        // fan-out dos sinais normais; só a réplica dona da conexão aplica (RealtimeConnectionRegistry.Dispatch),
        // e só se req.Seq for maior que o último comando aplicado para esta conexão (a corrida entre
        // publishes de enter/leave independentes não tem ordem garantida — o contador da aba resolve).
        await backplane.PublishAsync(new RealtimeEnvelope(
            RealtimeTarget.ForConnection(connectionId, userId, req.Scope, req.ResourceId, req.Seq),
            new RealtimeSignal(PresenceCommandKind.Enter, null)), ct);
        await NotifyChanged(backplane, req.Scope, req.ResourceId, ct);

        return TypedResults.NoContent();
    }

    internal static Task NotifyChanged(IRealtimeBackplane backplane, string scope, Guid resourceId, CancellationToken ct) =>
        backplane.PublishAsync(RealtimeEnvelope.PresenceChanged(scope, resourceId), ct);
}
