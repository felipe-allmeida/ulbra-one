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

    /// <summary>§7.2 do design: além de encerrar os canais, o drain "remove as entradas de presença das suas
    /// conexões — deploy não deixa avatar fantasma por 45 s". Sem isto, o TTL é o único caminho: cada réplica
    /// derrubada num rolling deploy deixa todo mundo que ela hospedava aparecendo como presente por até 45 s
    /// para quem está nas outras réplicas.
    /// <para>Ordem importa: as chaves são lidas ANTES de completar os canais (é o registry que sabe quem
    /// estava presente) e o sinal "presença mudou" vai DEPOIS da remoção, para que o refetch que ele provoca
    /// leia o estado já sem esta réplica.</para></summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        var presence = registry.LocalPresence();
        registry.CompleteAll();

        foreach (var key in presence)
        {
            try
            {
                await backplane.LeavePresenceAsync(key.Scope, key.ResourceId, key.UserId, cancellationToken);
                await backplane.PublishAsync(
                    RealtimeEnvelope.PresenceChanged(key.Scope, key.ResourceId), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Uma limpeza que falha não pode segurar o shutdown: o TTL ainda cobre este caso (é o
                // comportamento anterior a esta limpeza), e o processo precisa terminar dentro da janela de
                // drain do Container Apps.
                logger.LogWarning(ex,
                    "Falha ao limpar presença de {Scope}/{ResourceId} no drain; o TTL resolve em {Ttl}",
                    key.Scope, key.ResourceId, RealtimeDefaults.PresenceTtl);
            }
        }

        await base.StopAsync(cancellationToken);
    }
}
