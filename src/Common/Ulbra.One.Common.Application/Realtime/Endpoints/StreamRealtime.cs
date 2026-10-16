using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ulbra.One.Common.Application.Authentication;

namespace Ulbra.One.Common.Application.Realtime.Endpoints;

/// <summary>O canal servidor→cliente. Fica aberto: escreve cada sinal destinado a esta conexão e um
/// heartbeat a cada <see cref="RealtimeDefaults.Heartbeat"/>, que derruba conexão morta em proxy. A
/// renovação do TTL de presença é replica-wide, não por conexão — ver <c>RealtimeBackplaneListener</c>
/// (Common.Infrastructure) — então não acontece aqui.</summary>
public sealed class StreamRealtime : IEndpoint
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/stream", Handle).WithName("StreamRealtime").WithTags("Realtime")
            .WithSummary("Canal de atualizações em tempo real (SSE).")
            .WithDescription("Stream text/event-stream com os sinais destinados ao chamador. Cada sinal "
                + "indica apenas que algo mudou — o cliente refaz o fetch pelo endpoint de leitura "
                + "correspondente, que aplica a própria autorização.")
            .RequireAuthorization()
            .ExcludeFromDescription();

    private static async Task Handle(
        HttpContext context,
        UserContext userContext,
        RealtimeConnectionRegistry registry,
        CancellationToken ct)
    {
        context.Response.Headers.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        // Desliga o buffering de proxy reverso — sem isso o sinal fica preso até o buffer encher.
        context.Response.Headers["X-Accel-Buffering"] = "no";

        using var subscription = registry.Subscribe(
            Guid.Parse(userContext.UserId), userContext.Role, userContext.IsAdmin);

        // O heartbeat roda concorrente ao loop de sinais e ambos escrevem NA MESMA resposta, que não é
        // thread-safe — sem este gate as duas escritas se intercalam e corrompem o stream.
        using var write = new SemaphoreSlim(1, 1);
        using var heartbeat = new PeriodicTimer(RealtimeDefaults.Heartbeat);
        // O cliente precisa do próprio connectionId para chamar POST/DELETE /realtime/presence — não há
        // outro jeito de ele saber a que conexão o servidor associou o stream que acabou de abrir.
        await WriteAsync(context, $"data: {JsonSerializer.Serialize(
            new RealtimeSignal("connected", subscription.ConnectionId.ToString()), Json)}", write, ct);
        await WriteAsync(context, ": ping", write, ct);

        // O heartbeat liga a um CTS derivado de `ct`, cancelado no finally abaixo. Necessário porque o
        // loop de sinais tem uma SEGUNDA saída normal além do cancelamento de `ct`: o canal completando
        // (RealtimeBackplaneListener.StopAsync → registry.CompleteAll(), no drain). Nesse caso
        // ReadAllAsync retorna sem lançar, e sem o finally cancelando o heartbeat, `await beat` ficaria
        // esperando o próximo tique para sempre — o `using var subscription` nunca seria descartado, e
        // a conexão continuaria viva no registry indefinidamente.
        using var beatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var beat = HeartbeatLoop(context, heartbeat, write, beatCts.Token);
        try
        {
            await foreach (var signal in subscription.Reader.ReadAllAsync(ct))
                await WriteAsync(context, $"data: {JsonSerializer.Serialize(signal, Json)}", write, ct);
        }
        catch (OperationCanceledException)
        {
            // Cliente desconectou ou a réplica está drenando — saída normal.
        }
        finally
        {
            // finally (não só depois do catch): TODA saída do loop acima — cancelamento, canal
            // completado, ou qualquer outra exceção — precisa cancelar e esperar o heartbeat ANTES dos
            // `using` acima descartarem `heartbeat`/`write`, senão o heartbeat pode escrever num
            // SemaphoreSlim ou HttpContext já descartado.
            await beatCts.CancelAsync();
            await beat.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private static async Task HeartbeatLoop(
        HttpContext context,
        PeriodicTimer timer,
        SemaphoreSlim write,
        CancellationToken ct)
    {
        while (await timer.WaitForNextTickAsync(ct))
            await WriteAsync(context, ": ping", write, ct);
    }

    private static async Task WriteAsync(HttpContext context, string line, SemaphoreSlim write, CancellationToken ct)
    {
        await write.WaitAsync(ct);
        try
        {
            await context.Response.WriteAsync($"{line}\n\n", ct);
            await context.Response.Body.FlushAsync(ct);
        }
        finally
        {
            write.Release();
        }
    }
}
