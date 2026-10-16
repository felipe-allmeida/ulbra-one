using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Common.Application.Realtime.Endpoints;

/// <summary>Um usuário presente no recurso, já com o nome resolvido — o endpoint já autoriza contra o
/// próprio recurso (só entra/vê quem poderia abri-lo), então devolver o nome de quem está presente é a
/// mesma divulgação que o feature de presença existe para fazer, não uma nova.</summary>
public sealed record PresenceUserResponse(
    [property: SwaggerSchema("Id do usuário presente.")]
    Guid UserId,
    [property: SwaggerSchema("Nome de exibição do usuário. Cai para o próprio Guid, como string, se o "
        + "usuário foi removido entre ele entrar na presença e esta leitura.")]
    [property: SwaggerExample("Maria Silva")]
    string Name,
    [property: SwaggerSchema("Foto do perfil Google do usuário. Nula para quem não tem foto — o cliente cai "
        + "para as iniciais do nome.")]
    [property: SwaggerExample("https://lh3.googleusercontent.com/a/ACg8ocK")]
    string? PictureUrl);

/// <summary>Quem está presente num recurso. Devolve vazio (nunca erro) quando o chamador não pode ver o
/// recurso, OU quando o backplane está fora — presença é acessório e some sem quebrar a tela (§7.4 do
/// design). Um escopo DESCONHECIDO é diferente: isso não é "sem acesso", é um erro de chamada (rota
/// errada, typo, escopo que perdeu o authorizer) — recusa com o mesmo 400 que POST /presence usa, em vez
/// de devolver uma lista vazia indistinguível de "ninguém está presente".
/// <para>
/// <c>scope</c> e <c>resourceId</c> são anuláveis pelo mesmo motivo que os quatro parâmetros de
/// <see cref="LeavePresence"/>: um parâmetro de query NÃO anulável que o cliente OMITE faz o model binder do
/// ASP.NET lançar BadHttpRequestException ANTES do handler rodar, e UseAppExceptionHandler não trata essa
/// exceção — vira 500 general.unexpected, não o 400 que <c>.ProducesProblem</c> promete aqui embaixo.
/// Anulável mais validação explícita é o que transforma "campo ausente" (e "resourceId que não é Guid") em
/// erro de validação tipado.</para></summary>
public sealed class GetPresence : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/presence", Handle).WithName("GetPresence").WithTags("Realtime")
            .WithSummary("Lista quem está vendo um recurso agora.")
            .WithDescription("Usuários presentes, com nome já resolvido. Vazio quando ninguém está, "
                + "quando o chamador não pode ver o recurso, ou quando o backplane de realtime está "
                + "indisponível.")
            .RequireAuthorization()
            .Produces<IReadOnlyCollection<PresenceUserResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(
        [SwaggerSchema("Escopo do recurso, ex.: \"requisition\". Precisa ter um IPresenceAuthorizer "
            + "registrado — um escopo desconhecido é recusado (fail-closed).")]
        string? scope,
        // string?, não Guid?: com Guid? a OMISSÃO vira erro tipado, mas um valor que não é Guid ainda falha
        // no binder (TryParse false → BadHttpRequestException → 500). Receber cru e parsear aqui cobre os
        // dois casos com o mesmo 400 tipado.
        [SwaggerSchema("Id do recurso dentro do escopo (Guid).")]
        string? resourceId,
        IEnumerable<IPresenceAuthorizer> authorizers,
        IRealtimeBackplane backplane,
        IUserDirectory users,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(scope))
            return ErrorResults.ValidationError(
                new Dictionary<string, string[]> { ["scope"] = ["Escopo de presença desconhecido."] },
                ErrorCodes.Presence.UnknownScope);

        if (!Guid.TryParse(resourceId, out var resource))
            return ErrorResults.ValidationError(
                new Dictionary<string, string[]> { ["resourceId"] = ["Id de recurso inválido."] },
                ErrorCodes.Presence.InvalidResourceId);

        var authorizer = authorizers.FirstOrDefault(a => a.Scope == scope);
        if (authorizer is null)
            return ErrorResults.ValidationError(
                new Dictionary<string, string[]> { ["scope"] = ["Escopo de presença desconhecido."] },
                ErrorCodes.Presence.UnknownScope);

        if (!await authorizer.CanViewAsync(resource, ct))
            return TypedResults.Ok<IReadOnlyCollection<PresenceUserResponse>>([]);

        var userIds = await backplane.ReadPresenceAsync(scope, resource, ct);
        if (userIds.Count == 0) return TypedResults.Ok<IReadOnlyCollection<PresenceUserResponse>>([]);

        // Um id sem nome (usuário removido entre o enter e esta leitura) ainda aparece — com o próprio
        // Guid como rótulo — em vez de sumir da lista e deixar a contagem de presença mentir.
        var profiles = await users.GetProfilesAsync(userIds, ct);
        IReadOnlyCollection<PresenceUserResponse> result = userIds
            .Select(id => profiles.TryGetValue(id, out var p)
                ? new PresenceUserResponse(id, p.Name, p.PictureUrl)
                : new PresenceUserResponse(id, id.ToString(), null))
            .ToList();
        return TypedResults.Ok(result);
    }
}
