using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ulbra.One.Common.Application.Realtime;

namespace Ulbra.One.Common.Infrastructure.Realtime;

/// <summary>Liga o backplane ao registry: tudo que chega do Redis é entregue às conexões desta réplica.
/// No shutdown encerra os canais para que os loops SSE terminem em vez de morrerem no meio do drain.</summary>
public sealed class RealtimeBackplaneListener(
    IRealtimeBackplane backplane,
    RealtimeConnectionRegistry registry,
    ILogger<RealtimeBackplaneListener> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await backplane.SubscribeAsync(envelope =>
            {
                registry.Dispatch(envelope);
                return Task.CompletedTask;
            }, stoppingToken);

            // Renovação de presença é por RÉPLICA, não por conexão: LocalPresence() já enumera todas
            // as conexões locais, então chamar isso a partir do heartbeat de cada conexão seria O(N²)
            // (N passes de LocalPresence + N RenewPresenceAsync do conjunto inteiro a cada tique). Um
            // heartbeat único aqui renova tudo de uma vez — O(N) no total.
            using var timer = new PeriodicTimer(RealtimeDefaults.Heartbeat);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var keys = registry.LocalPresence();
                if (keys.Count == 0) continue;

                try
                {
                    await backplane.RenewPresenceAsync(keys, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Um blip no backplane (Redis) não pode derrubar este loop pro resto da vida da
                    // réplica — só perde uma renovação; a tentativa seguinte, um Heartbeat depois, resolve.
                    logger.LogWarning(ex, "Falha ao renovar presença desta réplica; tentando de novo no próximo heartbeat");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Encerramento normal via stoppingToken.
        }
        catch (Exception ex)
        {
            // Defesa em profundidade: realtime é acessório e não pode, em hipótese nenhuma, derrubar o
            // host — BackgroundServiceExceptionBehavior.StopHost (padrão do .NET) mata o processo
            // inteiro se uma exceção escapar de ExecuteAsync. RedisRealtimeBackplane já não deveria
            // lançar isso (SubscribeAsync é FireAndForget, os demais métodos têm catch próprio), mas
            // esta é a rede de segurança caso algo novo escape: loga em Error e encerra o loop em vez
            // de propagar. Realtime para nesta réplica até reiniciar; contas a pagar continua no ar.
            logger.LogError(ex, "RealtimeBackplaneListener encerrou por falha inesperada no backplane");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        registry.CompleteAll();
        await base.StopAsync(cancellationToken);
    }
}
