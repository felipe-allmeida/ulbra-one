namespace Ulbra.One.Common.Application.Realtime;

/// <summary>O que trafega entre réplicas. Só o <see cref="Signal"/> desce para o cliente.</summary>
public sealed record RealtimeEnvelope(RealtimeTarget Target, RealtimeSignal Signal);
