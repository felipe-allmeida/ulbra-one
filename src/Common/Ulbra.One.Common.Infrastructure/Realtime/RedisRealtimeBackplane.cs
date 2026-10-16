using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Ulbra.One.Common.Application.Realtime;

namespace Ulbra.One.Common.Infrastructure.Realtime;

public static class RealtimeRedisKeys
{
    public const string Channel = "realtime:signals";

    public static string Presence(string scope, Guid resourceId) => $"realtime:presence:{scope}:{resourceId}";
}

/// <summary>Backplane de produção. Faz duas coisas que fila de mensagem não faz junto: espalha o sinal
/// para todas as réplicas (PUB/SUB) e guarda presença como estado compartilhado legível por qualquer
/// réplica (sorted set com score de expiração). Ver §3.4 e §6.1 do design.</summary>
public sealed class RedisRealtimeBackplane(
    IConnectionMultiplexer redis, TimeProvider clock, ILogger<RedisRealtimeBackplane> logger) : IRealtimeBackplane
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task PublishAsync(RealtimeEnvelope envelope, CancellationToken ct)
    {
        try
        {
            // FireAndForget: publicar sinal é best-effort — não vale segurar o request esperando o ACK
            // do Redis (e, com o Redis fora, nem vale esperar o RedisConnectionException do round trip).
            await redis.GetSubscriber().PublishAsync(
                RedisChannel.Literal(RealtimeRedisKeys.Channel), JsonSerializer.Serialize(envelope, Json),
                CommandFlags.FireAndForget);
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
        {
            // Backplane fora ou lento (desconectado, ou conectado mas travado): publicar sinal é
            // best-effort por construção — o cliente se recupera no invalidate da próxima reconexão
            // (§7.4 do design).
        }
    }

    public async Task SubscribeAsync(Func<RealtimeEnvelope, Task> handler, CancellationToken ct) =>
        // FireAndForget: sem isso, um Redis inalcançável no boot deixa o SUBSCRIBE pendurado no backlog
        // até estourar RedisConnectionException — exceção que escapa do RealtimeBackplaneListener e
        // derruba o host inteiro (BackgroundServiceExceptionBehavior.StopHost é o padrão do .NET).
        // Com FireAndForget o SE.Redis não espera confirmação do servidor: registra a inscrição local e,
        // quando a conexão volta, reaplica automaticamente — não precisa (nem deve) de retry manual aqui.
        await redis.GetSubscriber().SubscribeAsync(RedisChannel.Literal(RealtimeRedisKeys.Channel), (channel, value) =>
        {
            if (value.IsNullOrEmpty) return;

            RealtimeEnvelope? envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<RealtimeEnvelope>((string)value!, Json);
            }
            catch (JsonException ex)
            {
                // Payload malformado no canal (ex.: outro tenant do mesmo Redis compartilhado) não pode
                // cegar esta réplica: o SE.Redis engole exceção de callback sem logar nada, então o log
                // é a única forma de perceber isso. A inscrição continua viva.
                logger.LogWarning(ex, "Sinal malformado recebido em {Channel}, ignorando", RealtimeRedisKeys.Channel);
                return;
            }
            if (envelope is null) return;

            var dispatch = handler(envelope);
            if (dispatch.IsCompleted) return;
            // O callback do SE.Redis não é awaited pelo chamador (não dá — a assinatura é síncrona), e
            // uma falha numa Task não observada morre em silêncio. Loga se o dispatch falhar depois.
            _ = dispatch.ContinueWith(
                t => logger.LogWarning(t.Exception, "Falha ao despachar sinal de realtime"),
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }, CommandFlags.FireAndForget);

    public async Task EnterPresenceAsync(string scope, Guid resourceId, Guid userId, CancellationToken ct)
    {
        try
        {
            var key = RealtimeRedisKeys.Presence(scope, resourceId);
            var db = redis.GetDatabase();
            // FireAndForget: entrar em presença é best-effort — o próximo RenewPresenceAsync (heartbeat
            // replica-wide) já reconcilia caso este write se perca com o Redis fora.
            await db.SortedSetAddAsync(key, userId.ToString(), Score, CommandFlags.FireAndForget);
            // TTL no conjunto inteiro: recurso abandonado não vaza mesmo se ninguém sair direito.
            await db.KeyExpireAsync(key, RealtimeDefaults.PresenceTtl * 2, CommandFlags.FireAndForget);
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
        {
            // Backplane fora ou lento: usuário simplesmente não aparece como presente até o Redis
            // voltar — não é motivo para falhar a ação que o usuário estava tentando fazer.
        }
    }

    public async Task RenewPresenceAsync(IReadOnlyCollection<PresenceKey> keys, CancellationToken ct)
    {
        try
        {
            var db = redis.GetDatabase();
            foreach (var group in keys.GroupBy(k => (k.Scope, k.ResourceId)))
            {
                var key = RealtimeRedisKeys.Presence(group.Key.Scope, group.Key.ResourceId);
                var entries = group.Select(k => new SortedSetEntry(k.UserId.ToString(), Score)).ToArray();
                // FireAndForget: renovar TTL é um heartbeat periódico — perder uma rodada porque o
                // Redis está fora é inofensivo, a rodada seguinte (um Heartbeat depois) resolve.
                await db.SortedSetAddAsync(key, entries, CommandFlags.FireAndForget);
                await db.KeyExpireAsync(key, RealtimeDefaults.PresenceTtl * 2, CommandFlags.FireAndForget);
            }
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
        {
            // Backplane fora ou lento: a presença desta réplica para de ser renovada e expira sozinha
            // no TTL — degradação aceitável, não uma falha a propagar (ver RealtimeBackplaneListener).
        }
    }

    public async Task LeavePresenceAsync(string scope, Guid resourceId, Guid userId, CancellationToken ct)
    {
        try
        {
            // FireAndForget: sair de presença é best-effort — se o Redis está fora, o TTL do conjunto
            // (EnterPresenceAsync) já garante que o usuário some sozinho quando o Redis voltar.
            await redis.GetDatabase().SortedSetRemoveAsync(
                RealtimeRedisKeys.Presence(scope, resourceId), userId.ToString(), CommandFlags.FireAndForget);
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
        {
            // Backplane fora ou lento: o usuário continua aparecendo como presente até o TTL vencer —
            // inofensivo, não é motivo para falhar a ação que o usuário estava tentando fazer.
        }
    }

    public async Task<IReadOnlyCollection<Guid>> ReadPresenceAsync(string scope, Guid resourceId, CancellationToken ct)
    {
        try
        {
            var key = RealtimeRedisKeys.Presence(scope, resourceId);
            var db = redis.GetDatabase();
            var now = Now;
            // Sem FireAndForget aqui: esta é a única operação que genuinamente precisa de uma resposta
            // (quem está presente agora), então mantém o round trip normal — o BacklogPolicy.FailFast
            // configurado em RealtimeSetup garante que, com o Redis fora, isso falha rápido em vez de
            // esperar o backlog inteiro.
            // Poda preguiçosa na leitura: quem venceu sai antes de responder.
            await db.SortedSetRemoveRangeByScoreAsync(key, double.NegativeInfinity, now);
            var members = await db.SortedSetRangeByScoreAsync(key, now, double.PositiveInfinity);
            return members.Select(m => Guid.Parse((string)m!)).ToArray();
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
        {
            // Backplane fora ou lento: presença some da tela, não quebra a tela (§7.4 do design).
            return [];
        }
    }

    private double Now => clock.GetUtcNow().ToUnixTimeMilliseconds();
    private double Score => (clock.GetUtcNow() + RealtimeDefaults.PresenceTtl).ToUnixTimeMilliseconds();
}
