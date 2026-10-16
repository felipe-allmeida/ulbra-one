namespace Ulbra.One.Common.Application.Realtime;

/// <summary>Os dois valores válidos de <see cref="RealtimeSignal.Kind"/> quando o envelope carrega um
/// comando de presença dirigido a uma conexão (<see cref="RealtimeTarget.ForConnection"/>). Nunca chegam
/// ao cliente — <see cref="RealtimeConnectionRegistry.Dispatch"/> intercepta esses envelopes antes de
/// escrever no canal SSE. Público (não internal) para os testes de roteamento entre réplicas
/// (Ulbra.One.UnitTests) referenciarem em vez de repetir os literais.</summary>
public static class PresenceCommandKind
{
    public const string Enter = "presence-cmd:enter";
    public const string Leave = "presence-cmd:leave";
}
