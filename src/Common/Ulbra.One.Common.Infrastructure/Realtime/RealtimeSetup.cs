using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Ulbra.One.Common.Application.Realtime;

namespace Ulbra.One.Common.Infrastructure.Realtime;

public static class RealtimeSetup
{
    public static IServiceCollection AddRealtime(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<RealtimeConnectionRegistry>();

        var redis = configuration["Redis:ConnectionString"];
        if (string.IsNullOrWhiteSpace(redis))
        {
            // Sem Redis configurado cai no backplane in-memory. Na prática isso é só o host de teste:
            // o compose e todos os ambientes implantados configuram Redis (§7.1 do design).
            services.AddSingleton<IRealtimeBackplane>(sp =>
                new InMemoryRealtimeBackplane(sp.GetRequiredService<TimeProvider>()));
        }
        else
        {
            // AbortOnConnectFail=false: o realtime é acessório. Redis fora não pode impedir a API de subir
            // nem de responder — contas a pagar não cai porque o cache caiu (§7.4 do design).
            // BacklogPolicy.FailFast: sem conexão, comandos falham na hora em vez de enfileirar no
            // backlog esperando uma reconexão que pode não vir — sem isso, cada chamada bloqueava pelo
            // SyncTimeout inteiro (~6s medidos) antes do catch em RedisRealtimeBackplane entrar em ação.
            var options = ConfigurationOptions.Parse(redis);
            options.AbortOnConnectFail = false;
            options.BacklogPolicy = BacklogPolicy.FailFast;
            // Fábrica, não instância pronta: registrar via factory faz o container ser DONO do
            // multiplexer (dispose automático no shutdown) e adia o connect para o primeiro uso, em vez
            // de bloquear Program.cs na inicialização.
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(options));
            services.AddSingleton<IRealtimeBackplane>(sp => new RedisRealtimeBackplane(
                sp.GetRequiredService<IConnectionMultiplexer>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILogger<RedisRealtimeBackplane>>()));
        }

        services.AddHostedService<RealtimeBackplaneListener>();
        return services;
    }
}
