using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Ulbra.One.Common.Infrastructure.Realtime;

/// <summary>Estado do backplane de realtime (§7.4 do design: "health check reporta degradado; alerta").
/// <para>
/// NUNCA devolve <see cref="HealthStatus.Unhealthy"/>, e é registrado FORA da tag <c>ready</c>: readiness
/// controla o tráfego que chega às réplicas, e realtime é acessório — um Redis fora tem que degradar o sino
/// e a presença, jamais tirar contas a pagar do ar. Degraded responde 200 no endpoint, então o sinal existe
/// para quem lê o corpo (e para o alerta) sem mexer no roteamento.</para>
/// <para>
/// Dois modos de falha, ambos silenciosos antes deste check: Redis configurado mas inalcançável, e
/// <c>Redis:ConnectionString</c> AUSENTE num ambiente implantado — este segundo é o pior dos dois, porque o
/// app sobe, responde 200 em tudo, e cada réplica fala sozinha com um backplane in-memory (com duas réplicas,
/// metade das notificações nunca chega ao browser do usuário).</para></summary>
public sealed class RealtimeBackplaneHealthCheck(IConnectionMultiplexer? redis, bool inMemoryIsExpected) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        if (redis is null)
            return inMemoryIsExpected
                ? HealthCheckResult.Healthy("Backplane in-memory (Development, sem Redis configurado).")
                : HealthCheckResult.Degraded(
                    "Redis:ConnectionString não configurado fora de Development — o realtime não cruza "
                    + "réplicas: sinais publicados numa réplica não chegam aos clientes conectados nas outras.");

        try
        {
            await redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            // Qualquer exceção vira Degraded, não só as de conexão: uma exceção que ESCAPA daqui é
            // relatada pelo middleware como Unhealthy, e Unhealthy neste check é justamente o que não
            // pode acontecer (503 no /health por causa do realtime). A exceção viaja no resultado.
            return HealthCheckResult.Degraded("Redis do backplane de realtime inalcançável.", ex);
        }
    }
}
