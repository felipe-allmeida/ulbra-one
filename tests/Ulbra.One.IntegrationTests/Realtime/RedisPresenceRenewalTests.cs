using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Testcontainers.Redis;
using Ulbra.One.Common.Application.Realtime;
using Ulbra.One.Common.Infrastructure.Realtime;

namespace Ulbra.One.IntegrationTests.Realtime;

/// <summary>Um Redis efêmero de verdade — a suíte inteira roda contra o backplane in-memory (ver
/// PresenceTests/RealtimeDegradationTests), que nunca exercitou <see cref="RedisRealtimeBackplane"/> com
/// Redis de verdade, e a reconciliação abaixo depende de semântica real do SortedSetAddAsync.</summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder().WithImage("redis:7-alpine").Build();
    public IConnectionMultiplexer Multiplexer { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Multiplexer = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        await Multiplexer.DisposeAsync();
        await _container.DisposeAsync();
    }
}

/// <summary>
/// Regressão da rodada 3 da revisão da Task 7. A rodada 2 tentou fechar a corrida enter/leave trocando
/// RenewPresenceAsync para <c>SortedSetAddAsync(..., When.Exists)</c> — só estende quem já está no set,
/// nunca cria. Isso trocou um bug por outro: <see cref="RedisRealtimeBackplane.EnterPresenceAsync"/> é um
/// ZADD <c>FireAndForget</c> sob <c>BacklogPolicy.FailFast</c>, então é o ÚNICO criador do membro — um
/// blip do Redis durante o POST perde esse write silenciosamente (o endpoint ainda responde 204), e com
/// XX nada mais consegue criar o membro depois: o usuário fica ausente da presença pelo resto daquela
/// tela, sem nunca se recuperar. Pior ainda numa queda &gt;45s: o score expira, uma leitura posterior poda
/// o membro, e depois que o Redis volta a renovação XX não tem mais nada para estender — o usuário some
/// para sempre enquanto o stream dele continua perfeitamente saudável.
/// <para>
/// A correção certa fecha a corrida do outro lado — um <c>seq</c> monotônico do cliente em
/// <see cref="RealtimeTarget.ForConnection"/>, aplicado em <c>RealtimeConnectionRegistry.ApplyPresenceCommand</c>
/// — e devolve a renovação ao comportamento capaz de criar (reconciliação), que é o que
/// <see cref="RedisRealtimeBackplane.EnterPresenceAsync"/> sempre presumiu ter disponível.
/// </para>
/// </summary>
public class RedisPresenceRenewalTests(RedisFixture fixture) : IClassFixture<RedisFixture>
{
    private RedisRealtimeBackplane NewBackplane() =>
        new(fixture.Multiplexer, TimeProvider.System, NullLogger<RedisRealtimeBackplane>.Instance);

    /// <summary>
    /// Guarda de regressão para o defeito que a rodada 2 introduziu: um membro que nunca chegou ao Redis
    /// (o ZADD de EnterPresenceAsync se perdeu — Redis fora no instante do POST, por exemplo) precisa ser
    /// recriado pela PRÓXIMA renovação, contanto que a réplica ainda tenha a entrada local viva. Sem
    /// isso, "voltar" nunca acontece: o comentário em EnterPresenceAsync ("o próximo RenewPresenceAsync
    /// já reconcilia") deixa de ser verdade.
    /// </summary>
    [Fact]
    public async Task Renovacao_recria_membro_ausente_do_redis_enquanto_entrada_local_esta_viva()
    {
        var backplane = NewBackplane();
        var scope = "requisition";
        var resource = Guid.NewGuid();
        var user = Guid.NewGuid();

        // Nunca chamou EnterPresenceAsync — simula o write perdido por FireAndForget (ou o score que já
        // expirou numa queda >45s do Redis, com o membro podado por uma leitura). A réplica ainda tem a
        // entrada local (por isso o heartbeat inclui esta chave), então a renovação precisa criar.
        await backplane.RenewPresenceAsync([new PresenceKey(scope, resource, user)], CancellationToken.None);

        Assert.Equal([user], await backplane.ReadPresenceAsync(scope, resource, CancellationToken.None));
    }

    [Fact]
    public async Task Renovacao_estende_quem_ja_esta_presente()
    {
        // Sanity: o caso normal — renovar quem realmente entrou — continua funcionando.
        var backplane = NewBackplane();
        var scope = "requisition";
        var resource = Guid.NewGuid();
        var user = Guid.NewGuid();
        await backplane.EnterPresenceAsync(scope, resource, user, CancellationToken.None);

        await backplane.RenewPresenceAsync([new PresenceKey(scope, resource, user)], CancellationToken.None);

        Assert.Equal([user], await backplane.ReadPresenceAsync(scope, resource, CancellationToken.None));
    }
}
