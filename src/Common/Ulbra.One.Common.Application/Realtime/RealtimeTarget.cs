namespace Ulbra.One.Common.Application.Realtime;

/// <summary>Roteamento server-side — quem deve receber o sinal. NUNCA é serializado para o cliente:
/// diz respeito à entrega, não ao conteúdo. Exatamente um modo é usado por envelope: usuário, role,
/// os presentes num recurso, ou uma conexão específica.</summary>
public sealed record RealtimeTarget(
    Guid? UserId, string? Role, string? PresenceScope, Guid? PresenceResourceId,
    Guid? ConnectionId = null, long? Seq = null)
{
    public static RealtimeTarget ForUser(Guid userId) => new(userId, null, null, null);
    public static RealtimeTarget ForRole(string role) => new(null, role, null, null);
    public static RealtimeTarget ForResource(string scope, Guid resourceId) => new(null, null, scope, resourceId);

    /// <summary>Comando interno de entrar/sair de presença dirigido a UMA conexão — nunca um sinal para
    /// o cliente (ver <see cref="RealtimeConnectionRegistry.Dispatch"/>, que intercepta antes de
    /// escrever no canal). Roteado pelo MESMO fan-out entre réplicas que os sinais normais usam, porque
    /// a conexão SSE de quem chama POST/DELETE /presence pode estar em outra réplica: sem afinidade de
    /// sessão (rejeitada no design) e com api_max_replicas=2 em prod, é 50/50 por requisição. Carrega o
    /// UserId de quem pediu — não para roteamento, mas para a réplica dona da conexão recusar um
    /// connectionId de outro usuário antes de aplicar (só ela sabe de quem é essa conexão).
    /// <para>
    /// <paramref name="seq"/> é o contador monotônico do CLIENTE (a mesma aba do navegador emite enter e
    /// leave em ordem, mesmo que os dois publishes cheguem fora de ordem entre réplicas — publishes
    /// independentes não têm garantia de ordem nenhuma). A réplica dona da conexão descarta qualquer
    /// comando cujo seq seja ≤ o último aplicado para essa (conexão, escopo, recurso) — ver
    /// <see cref="RealtimeConnectionRegistry"/>. Um relógio de réplica não serviria: réplicas não
    /// compartilham um relógio comum.</para></summary>
    public static RealtimeTarget ForConnection(
        Guid connectionId, Guid callerUserId, string scope, Guid resourceId, long seq) =>
        new(callerUserId, null, scope, resourceId, connectionId, seq);
}
