namespace Ulbra.One.Common.Application.Realtime;

public static class RealtimeDefaults
{
    /// <summary>Intervalo de dois relógios distintos, de propósito no mesmo valor: o heartbeat que cada
    /// conexão SSE escreve (derruba conexão morta em proxy) e o timer ÚNICO por réplica que renova o TTL da
    /// presença de todas as conexões locais (<see cref="RealtimeBackplaneListener"/>). A renovação foi tirada
    /// do tique de cada conexão porque lá era O(N²): LocalPresence() já enumera a réplica inteira. Dispensa
    /// heartbeat do cliente do mesmo jeito (§6.2 do design).</summary>
    public static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(20);

    /// <summary>Duas renovações perdidas. Réplica que morre deixa a presença vencer sozinha.</summary>
    public static readonly TimeSpan PresenceTtl = TimeSpan.FromSeconds(45);

    /// <summary>Sinais pendentes por conexão. Cheio, descarta o mais antigo — dois sinais coalescidos
    /// produzem o mesmo refetch que dois separados (§3.3).</summary>
    public const int ChannelCapacity = 32;
}
