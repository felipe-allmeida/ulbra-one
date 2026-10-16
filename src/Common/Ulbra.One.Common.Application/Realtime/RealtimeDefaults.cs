namespace Ulbra.One.Common.Application.Realtime;

public static class RealtimeDefaults
{
    /// <summary>Intervalo do heartbeat SSE. Derruba conexão morta em proxy e — no mesmo tique — renova o
    /// TTL da presença das conexões desta réplica, o que dispensa heartbeat do cliente (§6.2 do design).</summary>
    public static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(20);

    /// <summary>Duas renovações perdidas. Réplica que morre deixa a presença vencer sozinha.</summary>
    public static readonly TimeSpan PresenceTtl = TimeSpan.FromSeconds(45);

    /// <summary>Sinais pendentes por conexão. Cheio, descarta o mais antigo — dois sinais coalescidos
    /// produzem o mesmo refetch que dois separados (§3.3).</summary>
    public const int ChannelCapacity = 32;
}
