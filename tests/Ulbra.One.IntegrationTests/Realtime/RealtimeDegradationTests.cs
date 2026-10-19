using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ulbra.One.Common.Application.Realtime;
using Ulbra.One.Common.Application.Realtime.Endpoints;
using Ulbra.One.Common.Infrastructure.Realtime;
using Ulbra.One.IntegrationTests.Finance;

namespace Ulbra.One.IntegrationTests.Realtime;

/// <summary>Redis inalcançável não pode derrubar a API: realtime é acessório, contas a pagar não cai
/// porque o cache caiu. Aponta para um host que não resolve — o mesmo truque que o ApiFactory já usa
/// para o broker.</summary>
[Collection("Postgres")]
public class RealtimeDegradationTests(PostgresFixture fixture)
{
    private sealed class UnreachableRedisFactory(string connectionString) : ApiFactory(connectionString)
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Redis:ConnectionString"] = "unreachable-redis.invalid:6379" }));
            return base.CreateHost(builder);
        }
    }

    [Fact]
    public async Task Api_responde_normalmente_com_redis_fora()
    {
        // Uma chamada logo após o boot não prova nada: o defeito real (SUBSCRIBE preso no backlog do
        // SE.Redis até estourar RedisConnectionException, ~6s depois de subir, derrubando o host via
        // BackgroundServiceExceptionBehavior.StopHost) só aparece DEPOIS dessa janela. Este teste
        // precisa observar o host depois do timeout do backlog, não antes — senão passa mesmo quando
        // o crash está acontecendo, que é exatamente como o bug original escapou.
        await using var factory = new UnreachableRedisFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        var early = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, early.StatusCode);

        await Task.Delay(TimeSpan.FromSeconds(10));

        var lifetime = factory.Services.GetRequiredService<IHostApplicationLifetime>();
        Assert.False(lifetime.ApplicationStopping.IsCancellationRequested);

        var late = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, late.StatusCode);
    }

    [Fact]
    public async Task Presenca_retorna_vazia_com_redis_fora()
    {
        // Via DI direto no backplane, não HTTP: GET /realtime/presence devolve [] também quando o
        // autorizador nega — o que teria passado por um motivo que nada tem a ver com o Redis estar
        // fora. Chamar ReadPresenceAsync direto é o que de fato discrimina o comportamento de
        // degradação (o teste HTTP equivalente, com um recurso real e visível, está logo abaixo).
        await using var factory = new UnreachableRedisFactory(fixture.ConnectionString);
        var backplane = factory.Services.GetRequiredService<IRealtimeBackplane>();
        // Sem isso, um futuro fallback para in-memory quando a conexão falha manteria este teste verde
        // sem provar mais nada sobre degradação do Redis de verdade.
        Assert.IsType<RedisRealtimeBackplane>(backplane);

        var present = await backplane.ReadPresenceAsync("requisition", Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(present);
    }

    [Fact]
    public async Task Get_presenca_responde_200_vazio_com_redis_fora()
    {
        // Nível HTTP, complementando o teste acima que fala direto com o backplane: prova que o
        // ENDPOINT também degrada, não só a implementação por trás dele. O recurso precisa existir e
        // ser visível ao chamador — senão a resposta vazia viria do authorizer negando, o que não diz
        // nada sobre o Redis estar fora.
        await using var factory = new UnreachableRedisFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var unitId = await FinanceSeed.CreateUnitAsync(client, companyId, "Campus Teste", "CT");
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(
            client, companyId, $"CC{Random.Shared.Next(100_000, 999_999)}", "Marketing", unitId);
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Teste");

        var createRes = await client.PostAsJsonAsync("/api/requisitions", new
        {
            unitId,
            costCenterId,
            vendorId,
            type = "Purchase",
            description = "Comprar cadeiras",
            observation = (string?)null,
            estimateValue = 1_000m,
            estimateType = "Full",
            startCompetency = "2026-07",
            endCompetency = (string?)null,
            firstDueDate = "2026-07-10",
            paymentFrequency = (string?)null,
            paymentInstallmentsCount = (int?)null,
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var prefixedId = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString()!;
        // Presença trabalha com o Guid puro do recurso (ver PresenceKey/RequisitionPresenceAuthorizer),
        // não com o id prefixado ("req_...") que a API expõe nas rotas de Budgeting.
        var requisitionId = Guid.Parse(prefixedId["req_".Length..]);

        var res = await client.GetAsync($"/api/realtime/presence?scope=requisition&resourceId={requisitionId}");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        // PresenceUserResponse[], não Guid[]: o endpoint devolve id + nome desde a Task 8. Com Guid[] este
        // teste passava só porque o array vem vazio — e continuaria passando com o contrato quebrado.
        var users = await res.Content.ReadFromJsonAsync<PresenceUserResponse[]>();
        Assert.Empty(users!);
    }

    /// <summary>§7.4 do design: "health check reporta degradado; alerta". Com o Redis inalcançável o check do
    /// backplane precisa DIZER isso — em Degraded, nunca em Unhealthy — e precisa ficar FORA do readiness, que
    /// é o que decide se a réplica recebe tráfego: realtime é acessório e não pode tirar contas a pagar do ar.
    /// <para>Nada aqui checa o status HTTP: o check "database" (tag ready) aponta para a connection string da
    /// configuração, não para o Postgres do Testcontainers, então /health e /health/ready já respondem 503
    /// neste host de teste por um motivo que nada tem a ver com realtime. O que discrimina o comportamento é o
    /// CORPO: qual check aparece em qual endpoint, e com que status.</para></summary>
    [Fact]
    public async Task Health_reporta_backplane_degradado_e_fora_do_readiness()
    {
        await using var factory = new UnreachableRedisFactory(fixture.ConnectionString);
        var client = factory.CreateClient();

        var ready = await client.GetAsync("/health/ready");
        Assert.DoesNotContain("realtime-backplane", await ready.Content.ReadAsStringAsync());

        var health = await client.GetAsync("/health");
        var body = JsonDocument.Parse(await health.Content.ReadAsStringAsync()).RootElement;
        // Degraded, não Unhealthy: é o que mantém o realtime incapaz de virar 503 quando ele é o único
        // check com problema.
        Assert.Equal("Degraded", body.GetProperty("entries").GetProperty("realtime-backplane")
            .GetProperty("status").GetString());
    }
}
