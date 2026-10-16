namespace Ulbra.One.Common.Application.Realtime;

/// <summary>O que desce no fio SSE. Deliberadamente magro: diz que algo mudou, nunca o que mudou —
/// o cliente refaz o fetch pelo endpoint de leitura, que já aplica autorização. Ver §3.3 do design.</summary>
/// <param name="Kind">"connected", "notification" ou "presence".</param>
/// <param name="ResourceId">Alvo do sinal quando há um (id da requisição, por exemplo).</param>
public sealed record RealtimeSignal(string Kind, string? ResourceId);
