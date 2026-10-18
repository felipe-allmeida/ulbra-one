using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Ulbra.One.Common.Application.Realtime;

namespace Ulbra.One.Common.Infrastructure.Realtime;

public static class RealtimeSetup
{
    /// <summary>Tag do health check do backplane. Deliberadamente NÃO é <c>ready</c>: ver
    /// <see cref="RealtimeBackplaneHealthCheck"/>.</summary>
    public const string HealthCheckTag = "realtime";

    public static IServiceCollection AddRealtime(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<RealtimeConnectionRegistry>();

        var redis = configuration["Redis:ConnectionString"];
        if (string.IsNullOrWhiteSpace(redis))
        {
            // Sem Redis configurado cai no backplane in-memory: a API sobe e funciona, mas o realtime deixa
            // de cruzar réplicas em silêncio. Isso é o esperado no host de teste; em qualquer ambiente
            // implantado (o compose e os três do Azure configuram Redis, §7.1 do design) significa segredo
            // faltando no Key Vault, typo, ou KeyVault:Uri não resolvendo — daí o Warning aqui e o Degraded
            // no health check, em vez de recusar o boot: §7.4 é explícito que a API não deixa de subir por
            // causa do realtime.
            var inMemoryIsExpected = environment.IsDevelopment();
            services.AddSingleton<IRealtimeBackplane>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<InMemoryRealtimeBackplane>>();
                if (inMemoryIsExpected)
                    logger.LogInformation(
                        "Redis:ConnectionString ausente — realtime usando backplane in-memory (Development).");
                else
                    logger.LogWarning(
                        "Redis:ConnectionString ausente em {Environment} — realtime caiu para o backplane "
                        + "in-memory: sinais NÃO cruzam réplicas e presença some entre elas. Health check "
                        + "reporta degradado.", environment.EnvironmentName);

                return new InMemoryRealtimeBackplane(sp.GetRequiredService<TimeProvider>());
            });
            services.AddHealthChecks().Add(new HealthCheckRegistration(
                "realtime-backplane",
                _ => new RealtimeBackplaneHealthCheck(null, inMemoryIsExpected),
                HealthStatus.Degraded,
                [HealthCheckTag]));
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
            services.AddHealthChecks().Add(new HealthCheckRegistration(
                "realtime-backplane",
                sp => new RealtimeBackplaneHealthCheck(
                    sp.GetRequiredService<IConnectionMultiplexer>(), inMemoryIsExpected: false),
                HealthStatus.Degraded,
                [HealthCheckTag]));
        }

        services.AddHostedService<RealtimeBackplaneListener>();
        return services;
    }
}
