using Microsoft.Extensions.Diagnostics.HealthChecks;
using Ulbra.One.Common.Infrastructure.Realtime;
using Xunit;

namespace Ulbra.One.UnitTests.Realtime;

/// <summary>O §7.4 do design termina em "health check reporta degradado; alerta". O modo de falha que este
/// teste cobre é o mais silencioso dos dois: <c>Redis:ConnectionString</c> AUSENTE num ambiente implantado —
/// a app sobe, responde 200 em tudo, e cada réplica fala sozinha com um backplane in-memory. Sem este check
/// nada distingue isso de um sistema saudável.</summary>
public class RealtimeBackplaneHealthCheckTests
{
    private static readonly HealthCheckContext Context = new()
    {
        Registration = new HealthCheckRegistration("realtime-backplane", _ => null!, HealthStatus.Degraded, []),
    };

    [Fact]
    public async Task Backplane_in_memory_fora_de_development_reporta_degradado()
    {
        var check = new RealtimeBackplaneHealthCheck(redis: null, inMemoryIsExpected: false);

        var result = await check.CheckHealthAsync(Context);

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Fact]
    public async Task Backplane_in_memory_em_development_e_saudavel()
    {
        var check = new RealtimeBackplaneHealthCheck(redis: null, inMemoryIsExpected: true);

        var result = await check.CheckHealthAsync(Context);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }
}
