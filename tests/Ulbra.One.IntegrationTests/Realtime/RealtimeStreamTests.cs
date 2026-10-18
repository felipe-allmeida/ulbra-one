using System.Net;
using Ulbra.One.Common.Application.Realtime;
using Microsoft.Extensions.DependencyInjection;

namespace Ulbra.One.IntegrationTests.Realtime;

[Collection("Postgres")]
public class RealtimeStreamTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Stream_exige_autenticacao()
    {
        var factory = fixture.Api;
        var client = factory.CreateClient();

        var res = await client.GetAsync("/api/realtime/stream", HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Stream_entrega_sinal_publicado_no_backplane()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var res = await client.GetAsync("/api/realtime/stream", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("text/event-stream", res.Content.Headers.ContentType?.MediaType);

        await using var stream = await res.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);

        // A conexão precisa estar registrada antes do publish, senão o sinal não tem para quem ir.
        // O heartbeat inicial é escrito depois do registro, então recebê-lo prova que já registrou.
        // Lê ATÉ o ping em vez de afirmar a primeira linha: a ordem dos eventos de abertura não é
        // requisito, e uma task posterior passa a emitir "connected" antes dele.
        string? line;
        while ((line = await reader.ReadLineAsync(cts.Token)) != ": ping") Assert.NotNull(line);

        var backplane = factory.Services.GetRequiredService<IRealtimeBackplane>();
        await backplane.PublishAsync(
            new RealtimeEnvelope(RealtimeTarget.ForRole("Admin"), new RealtimeSignal("notification", null)),
            cts.Token);

        while ((line = await reader.ReadLineAsync(cts.Token)) is not null)
            if (line.StartsWith("data: ")) break;

        Assert.Equal("""data: {"kind":"notification","resourceId":null}""", line);
    }

    [Fact]
    public async Task Stream_encerra_quando_o_registry_completa_os_canais_no_drain()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var res = await client.GetAsync("/api/realtime/stream", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        await using var stream = await res.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(cts.Token)) != ": ping") Assert.NotNull(line);

        // Reproduz exatamente o que RealtimeBackplaneListener.StopAsync faz no drain: completa os
        // canais de todas as conexões desta réplica SEM cancelar o `ct` da requisição — a saída
        // "normal" (sem exceção) do loop de sinais. Antes do fix (Finding 1), essa saída não cancelava
        // o heartbeat, então `await beat` ficava esperando o próximo tique para sempre: Handle nunca
        // retornava, a resposta HTTP nunca fechava, e a subscription nunca era descartada do registry.
        var registry = factory.Services.GetRequiredService<RealtimeConnectionRegistry>();
        registry.CompleteAll();

        using var drainCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            // Drena qualquer linha residual (a quebra em branco após o ": ping" já lido, por exemplo)
            // até o fim do stream. Se Handle retornar (fix aplicado), a resposta HTTP fecha e isso
            // termina com null bem antes dos 5s. Se Handle ficar preso (bug do Finding 1), isso nunca
            // recebe null e o teste falha por cancelamento do drainCts, não por timeout do xunit.
            while (await reader.ReadLineAsync(drainCts.Token) is not null) { }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("O stream não fechou 5s depois de registry.CompleteAll() — Handle ficou preso "
                + "esperando o heartbeat (Finding 1: a subscription nunca é descartada no drain).");
        }

        // A conexão da stream nunca chamou EnterPresence, então LocalPresence() já seria vazio mesmo
        // com a subscription vazada — a prova real do fix é o stream ter fechado acima. Este assert
        // fica só para deixar explícito que o registry segue saudável (sem exceção) depois do drain.
        Assert.Empty(registry.LocalPresence());
    }
}
