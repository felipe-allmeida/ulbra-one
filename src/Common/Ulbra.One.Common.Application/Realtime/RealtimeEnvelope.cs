namespace Ulbra.One.Common.Application.Realtime;

/// <summary>O que trafega entre réplicas. Só o <see cref="Signal"/> desce para o cliente.</summary>
public sealed record RealtimeEnvelope(RealtimeTarget Target, RealtimeSignal Signal)
{
    /// <summary>"A presença deste recurso mudou" — o sinal que faz quem está vendo o recurso refazer o
    /// fetch da lista. Fábrica aqui, e não montado no call site, porque o publica de dois lugares
    /// distantes (os endpoints de presença e a limpeza de drain em RealtimeBackplaneListener) e o Kind
    /// precisa ser exatamente o que o cliente escuta.</summary>
    public static RealtimeEnvelope PresenceChanged(string scope, Guid resourceId) =>
        new(RealtimeTarget.ForResource(scope, resourceId), new RealtimeSignal("presence", resourceId.ToString()));
}
