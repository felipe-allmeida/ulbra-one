using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Realtime;
using Ulbra.One.Common.Application.Realtime.Endpoints;
using Ulbra.One.Identity.Domain;
using Ulbra.One.IntegrationTests.Finance;

namespace Ulbra.One.IntegrationTests.Realtime;

[Collection("Postgres")]
public class PresenceTests(PostgresFixture fixture)
{
    private sealed record PresenceRequest(string ConnectionId, string Scope, Guid ResourceId, long Seq = 1);

    /// <summary>
    /// Um stream SSE de verdade, aberto contra <c>/api/realtime/stream</c>, com o connectionId real
    /// extraído do evento "connected" — não um <see cref="Guid.NewGuid"/> qualquer. Um id inventado não
    /// pertence a conexão nenhuma no registry, então POST /presence com ele sempre retorna 204 mesmo se
    /// o publish pelo backplane (CRITICAL 2 da revisão) virasse um no-op — o round trip via
    /// GET /presence (que lê o set do Redis, não o registry) passaria do mesmo jeito. Só um connectionId
    /// real, de uma conexão de fato registrada, expõe essa lacuna.
    /// </summary>
    private sealed class SseConnection : IAsyncDisposable
    {
        private readonly HttpResponseMessage _response;
        private readonly Stream _stream;
        private readonly StreamReader _reader;

        public Guid ConnectionId { get; }

        private SseConnection(HttpResponseMessage response, Stream stream, StreamReader reader, Guid connectionId)
        {
            _response = response;
            _stream = stream;
            _reader = reader;
            ConnectionId = connectionId;
        }

        public static async Task<SseConnection> OpenAsync(HttpClient client, CancellationToken ct)
        {
            var response = await client.GetAsync(
                "/api/realtime/stream", HttpCompletionOption.ResponseHeadersRead, ct);
            var stream = await response.Content.ReadAsStreamAsync(ct);
            var reader = new StreamReader(stream);

            // O primeiro evento "data: " é sempre "connected" (StreamRealtime.cs), escrito antes do
            // ": ping" inicial — é assim que o cliente aprende o id da própria conexão.
            string? line;
            while ((line = await reader.ReadLineAsync(ct)) is not null && !line.StartsWith("data: ", StringComparison.Ordinal)) { }
            Assert.NotNull(line);
            var json = JsonDocument.Parse(line!["data: ".Length..]);
            Assert.Equal("connected", json.RootElement.GetProperty("kind").GetString());
            var connectionId = Guid.Parse(json.RootElement.GetProperty("resourceId").GetString()!);

            return new SseConnection(response, stream, reader, connectionId);
        }

        /// <summary>Lê a próxima linha "data: " dentro da janela, ou null se nada chegar até o timeout —
        /// usado tanto para confirmar UM sinal quanto para provar a AUSÊNCIA de um.</summary>
        public async Task<string?> TryReadNextSignalAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                string? line;
                while ((line = await _reader.ReadLineAsync(cts.Token)) is not null)
                    if (line.StartsWith("data: ", StringComparison.Ordinal)) return line;
                return null;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            _reader.Dispose();
            await _stream.DisposeAsync();
            _response.Dispose();
        }
    }

    /// <summary>Cria empresa/unidade/centro de custo/fornecedor e uma requisição real, e devolve o Guid
    /// PURO do recurso — presença trabalha com ele, não com o id prefixado ("req_...") que a API expõe
    /// nas rotas de Budgeting (ver PresenceKey/RequisitionPresenceAuthorizer).</summary>
    private static async Task<(Guid CompanyId, Guid UnitId, Guid CostCenterId, Guid RequisitionId)> SeedRequisitionAsync(HttpClient admin)
    {
        var companyId = await FinanceSeed.CreateCompanyAsync(admin);
        var unitId = await FinanceSeed.CreateUnitAsync(admin, companyId, "Campus Teste", "CT");
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(
            admin, companyId, $"CC{Random.Shared.Next(100_000, 999_999)}", "Marketing", unitId);
        var vendorId = await FinanceSeed.CreateVendorAsync(admin, "Fornecedor Teste");

        var res = await admin.PostAsJsonAsync("/api/requisitions", new
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
        var prefixedId = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        return (
            CompanyId.Parse(companyId, null).Value,
            UnitId.Parse(unitId, null).Value,
            CostCenterId.Parse(costCenterId, null).Value,
            Guid.Parse(prefixedId["req_".Length..]));
    }

    [Fact]
    public async Task Escopo_desconhecido_e_recusado()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();

        var res = await client.PostAsJsonAsync("/api/realtime/presence",
            new PresenceRequest(Guid.NewGuid().ToString(), "escopo-inexistente", Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Escopo_desconhecido_e_recusado_tambem_na_leitura()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();

        var res = await client.GetAsync(
            $"/api/realtime/presence?scope=escopo-inexistente&resourceId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    /// <summary>Espelho, na LEITURA, do que os testes de omissão de LeavePresence cobrem no DELETE: um
    /// parâmetro de query não anulável que o cliente OMITE (ou manda malformado) faz o binder lançar
    /// BadHttpRequestException antes do handler, e isso vira 500 general.unexpected — enquanto o
    /// .ProducesProblem(400) do endpoint promete outra coisa. Os três casos precisam do 400 tipado.</summary>
    [Fact]
    public async Task Scope_omitido_da_query_e_recusado_na_leitura()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();

        var res = await client.GetAsync($"/api/realtime/presence?resourceId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Resource_id_omitido_da_query_e_recusado_na_leitura()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();

        var res = await client.GetAsync("/api/realtime/presence?scope=requisition");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Resource_id_malformado_e_recusado_na_leitura()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();

        var res = await client.GetAsync("/api/realtime/presence?scope=requisition&resourceId=nao-e-um-guid");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Presenca_exige_autenticacao()
    {
        var factory = fixture.Api;
        var client = factory.CreateClient();

        var res = await client.PostAsJsonAsync("/api/realtime/presence",
            new PresenceRequest(Guid.NewGuid().ToString(), "requisition", Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Requisicao_inexistente_nao_entra_na_presenca()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();

        var res = await client.PostAsJsonAsync("/api/realtime/presence",
            new PresenceRequest(Guid.NewGuid().ToString(), "requisition", Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Presenca_vazia_quando_ninguem_entrou()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();

        var users = await client.GetFromJsonAsync<PresenceUserResponse[]>(
            $"/api/realtime/presence?scope=requisition&resourceId={Guid.NewGuid()}");

        Assert.Empty(users!);
    }

    [Fact]
    public async Task Connection_id_malformado_e_recusado()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var (_, _, _, requisitionId) = await SeedRequisitionAsync(client);

        var res = await client.PostAsJsonAsync("/api/realtime/presence",
            new PresenceRequest("nao-e-um-guid", "requisition", requisitionId));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    /// <summary>
    /// Documenta o contrato exato que a revisão da Task 8 (frontend) descobriu faltando: o CORPO JSON
    /// (System.Text.Json) só aceita <see cref="Guid"/> na forma "D" (hifenizada, ex.
    /// <c>3fa85f64-5717-4562-b3fc-2c963f66afa6</c>) — a forma "N" (32 hex sem hífen, que é exatamente
    /// como um id tipado como <c>req_{guid:N}</c> chega depois de só tirar o prefixo) falha a
    /// desserialização. Isto é diferente de <see cref="Connection_id_malformado_e_recusado"/> (uma
    /// string que não é Guid nenhum) — aqui o valor É um Guid válido, só numa forma que o binder do
    /// corpo JSON não aceita (ao contrário de <c>Guid.TryParse</c>, usado pelo binding de query em
    /// GET/DELETE, que aceita as duas formas — por isso o cliente nunca via este problema testando só
    /// a leitura). O frontend (`web/src/realtime/usePresence.ts`) normaliza pra forma D antes de
    /// montar o corpo; este teste é o que impede essa normalização de regredir sem ser notada.
    /// </summary>
    [Fact]
    public async Task Resource_id_na_forma_N_no_corpo_json_e_recusado()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var (_, _, _, requisitionId) = await SeedRequisitionAsync(client);

        var res = await client.PostAsJsonAsync("/api/realtime/presence", new
        {
            connectionId = Guid.NewGuid().ToString(),
            scope = "requisition",
            resourceId = requisitionId.ToString("N"), // 32 hex, sem hífen — a forma que falha
            seq = 1,
        });

        // O model binder do corpo JSON rejeita a desserialização antes do handler rodar — nem
        // UseAppExceptionHandler nem o handler entram em ação, então isto não é o 400 tipado
        // (ErrorResults.ValidationError) dos outros testes: é um erro de framework, tipicamente 400 em
        // produção e 500 (com ThrowOnBadRequest=true) em Development. O que importa aqui é que NUNCA é
        // 204 — a forma N nunca deve ser aceita silenciosamente.
        Assert.NotEqual(HttpStatusCode.NoContent, res.StatusCode);
    }

    /// <summary>
    /// Fecha o buraco de presença fantasma da revisão da Task 7 (rodada 4): Seq é um long simples no
    /// corpo, sem [Required] — um cliente que omite o campo (ou o zera) bina em 0 sem erro nenhum. Sem
    /// esta checagem, um enter(seq=0) aplicaria normalmente e um leave(seq=0) subsequente seria
    /// descartado em silêncio por TryAdvanceSeq (0 &lt;= 0), deixando o usuário "presente" pelo resto da
    /// vida do stream — a renovação capaz de recriar (restaurada na rodada 3) reforça essa entrada
    /// fantasma a cada heartbeat em vez de deixá-la expirar.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Seq_zero_ou_negativo_e_recusado_ao_entrar(long seq)
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var (_, _, _, requisitionId) = await SeedRequisitionAsync(client);

        var res = await client.PostAsJsonAsync("/api/realtime/presence",
            new PresenceRequest(Guid.NewGuid().ToString(), "requisition", requisitionId, seq));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Seq_zero_ou_negativo_e_recusado_ao_sair(long seq)
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();

        var res = await client.DeleteAsync(
            $"/api/realtime/presence?connectionId={Guid.NewGuid()}&scope=requisition&resourceId={Guid.NewGuid()}&seq={seq}");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    /// <summary>
    /// A lacuna real da rodada 5: connectionId/scope/resourceId/seq em LeavePresence vêm da QUERY
    /// STRING, não de um corpo JSON — um parâmetro de query NÃO anulável que o cliente OMITE por
    /// completo faz o model binder do ASP.NET lançar BadHttpRequestException antes do handler rodar, e
    /// UseAppExceptionHandler não trata essa exceção especificamente, então virava 500
    /// general.unexpected — pior que o 400 que a rodada 4 tentou garantir. Os quatro parâmetros agora
    /// são anuláveis e validados explicitamente; este é o teste que prova a omissão de verdade (não só
    /// um valor 0 explícito, já coberto acima) — deve falhar se a checagem de nulo for removida.
    /// </summary>
    [Fact]
    public async Task Seq_omitido_da_query_e_recusado_ao_sair()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();

        var res = await client.DeleteAsync(
            $"/api/realtime/presence?connectionId={Guid.NewGuid()}&scope=requisition&resourceId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Connection_id_omitido_da_query_e_recusado_ao_sair()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();

        var res = await client.DeleteAsync(
            $"/api/realtime/presence?scope=requisition&resourceId={Guid.NewGuid()}&seq=1");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Scope_omitido_da_query_e_recusado_ao_sair()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();

        var res = await client.DeleteAsync(
            $"/api/realtime/presence?connectionId={Guid.NewGuid()}&resourceId={Guid.NewGuid()}&seq=1");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Resource_id_omitido_da_query_e_recusado_ao_sair()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();

        var res = await client.DeleteAsync(
            $"/api/realtime/presence?connectionId={Guid.NewGuid()}&scope=requisition&seq=1");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    /// <summary>
    /// Distinto dos dois testes acima: aqueles mandam Seq EXPLICITAMENTE 0/-1 via o record da própria
    /// suíte (que tem um default `= 1` só para não obrigar toda chamada existente a mudar). Este manda
    /// um corpo JSON sem a chave "seq" NENHUMA — o cenário real do cliente que a revisão descreveu
    /// ("omite o campo"). O long simples sem [Required] faz o binder da API preencher com 0 do mesmo
    /// jeito, então a mesma checagem pega os dois casos — mas só este prova que a omissão de verdade
    /// (não só um valor 0 explícito) retorna 400 em vez de silenciosamente virar 0.
    /// </summary>
    [Fact]
    public async Task Seq_omitido_do_corpo_e_recusado_ao_entrar()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var (_, _, _, requisitionId) = await SeedRequisitionAsync(client);

        var res = await client.PostAsJsonAsync("/api/realtime/presence", new
        {
            connectionId = Guid.NewGuid().ToString(),
            scope = "requisition",
            resourceId = requisitionId,
            // seq deliberadamente ausente do JSON.
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    /// <summary>
    /// A checagem central de privacidade da Task 7: presença espelha a autorização de leitura de
    /// GetRequisition, então um Requester escopado só entra/vê presença dentro do seu próprio Empresa ×
    /// Unidade × Centro de Custo. Um teste que só cobre "id aleatório" (Requisicao_inexistente_...) passa
    /// igualzinho contra um autorizador que devolve true para tudo que existe — este cobre a diferença:
    /// a requisição EXISTE e o Requester está na empresa e unidade certas, só o centro de custo diverge.
    /// </summary>
    [Fact]
    public async Task Requester_fora_do_escopo_nao_entra_na_presenca()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync();
        var (companyId, unitId, _, requisitionId) = await SeedRequisitionAsync(admin);

        var outsider = factory.CreateClientWithUnitScope(
            Roles.Requester, [companyId], [(companyId, unitId)], [(companyId, Guid.NewGuid())],
            Permissions.RequisitionsRead);

        var res = await outsider.PostAsJsonAsync("/api/realtime/presence",
            new PresenceRequest(Guid.NewGuid().ToString(), "requisition", requisitionId));

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Requester_fora_do_escopo_ve_presenca_vazia()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync();
        var (companyId, unitId, _, requisitionId) = await SeedRequisitionAsync(admin);

        var outsider = factory.CreateClientWithUnitScope(
            Roles.Requester, [companyId], [(companyId, unitId)], [(companyId, Guid.NewGuid())],
            Permissions.RequisitionsRead);

        var users = await outsider.GetFromJsonAsync<PresenceUserResponse[]>(
            $"/api/realtime/presence?scope=requisition&resourceId={requisitionId}");

        Assert.Empty(users!);
    }

    [Fact]
    public async Task Requester_dentro_do_escopo_entra_na_presenca()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync();
        var (companyId, unitId, costCenterId, requisitionId) = await SeedRequisitionAsync(admin);

        var insider = factory.CreateClientWithUnitScope(
            Roles.Requester, [companyId], [(companyId, unitId)], [(companyId, costCenterId)],
            Permissions.RequisitionsRead);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var connection = await SseConnection.OpenAsync(insider, cts.Token);

        var res = await insider.PostAsJsonAsync("/api/realtime/presence",
            new PresenceRequest(connection.ConnectionId.ToString(), "requisition", requisitionId));

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
    }

    [Fact]
    public async Task Sair_exige_autenticacao()
    {
        var factory = fixture.Api;
        var client = factory.CreateClient();

        var res = await client.DeleteAsync(
            $"/api/realtime/presence?connectionId={Guid.NewGuid()}&scope=requisition&resourceId={Guid.NewGuid()}&seq=1");

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Sair_nao_exige_ver_o_recurso()
    {
        // A remoção em si não autoriza contra o recurso (sair é sempre seguro e idempotente) — um id
        // que não existe, ou que o chamador não poderia ver, ainda assim responde 204.
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();

        var res = await client.DeleteAsync(
            $"/api/realtime/presence?connectionId={Guid.NewGuid()}&scope=requisition&resourceId={Guid.NewGuid()}&seq=1");

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
    }

    [Fact]
    public async Task Entrar_e_sair_completa_o_ciclo_de_presenca()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var (_, _, _, requisitionId) = await SeedRequisitionAsync(client);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var connection = await SseConnection.OpenAsync(client, cts.Token);

        var enter = await client.PostAsJsonAsync("/api/realtime/presence",
            new PresenceRequest(connection.ConnectionId.ToString(), "requisition", requisitionId, Seq: 1));
        Assert.Equal(HttpStatusCode.NoContent, enter.StatusCode);

        var afterEnter = await client.GetFromJsonAsync<PresenceUserResponse[]>(
            $"/api/realtime/presence?scope=requisition&resourceId={requisitionId}");
        var presentUser = Assert.Single(afterEnter!);
        // Task 8 da revisão do frontend: o nome já vem resolvido (IUserDirectory), não só o Guid — é o
        // que o avatar de presença mostra no title.
        Assert.Equal("Admin Teste", presentUser.Name);

        var leave = await client.DeleteAsync(
            $"/api/realtime/presence?connectionId={connection.ConnectionId}&scope=requisition&resourceId={requisitionId}&seq=2");
        Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);

        var afterLeave = await client.GetFromJsonAsync<PresenceUserResponse[]>(
            $"/api/realtime/presence?scope=requisition&resourceId={requisitionId}");
        Assert.Empty(afterLeave!);
    }

    /// <summary>
    /// Fecha a lacuna encontrada na 2ª rodada de revisão da Task 7 (NEW 2): as duas coisas acima —
    /// 204 no POST e a leitura via GET /presence — só provam o set do Redis. Nenhuma delas passa pelo
    /// RealtimeConnectionRegistry desta réplica, então um publish trocado por um no-op (reintroduzindo a
    /// regressão CRITICAL 2 inteira) deixava as duas passando do mesmo jeito. Este teste usa um
    /// connectionId REAL, de uma conexão de fato aberta, e inspeciona o registry diretamente — o único
    /// jeito de provar que o comando ForConnection saiu do endpoint e chegou lá.
    /// </summary>
    [Fact]
    public async Task Entrar_via_endpoint_chega_na_conexao_real_da_stream()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var (_, _, _, requisitionId) = await SeedRequisitionAsync(client);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var connection = await SseConnection.OpenAsync(client, cts.Token);

        var res = await client.PostAsJsonAsync("/api/realtime/presence",
            new PresenceRequest(connection.ConnectionId.ToString(), "requisition", requisitionId));
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        var registry = factory.Services.GetRequiredService<RealtimeConnectionRegistry>();
        Assert.Contains(registry.LocalPresence(), k => k.Scope == "requisition" && k.ResourceId == requisitionId);
    }

    /// <summary>
    /// IMPORTANT 1 da revisão: sem o gate de autorização em LeavePresence, qualquer chamador autenticado
    /// publicava um sinal "presença mudou" em QUALQUER (scope, resourceId), forçando um refetch em quem
    /// está vendo um recurso que ele nem pode abrir. O teste HTTP de status (Sair_nao_exige_ver_o_recurso)
    /// não distingue "removeu e não publicou" de "removeu e publicou mesmo assim" — só ler o stream de
    /// quem está presente prova a ausência do sinal.
    /// </summary>
    [Fact]
    public async Task Sair_sem_autorizacao_nao_publica_sinal_de_mudanca()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync();
        var (companyId, unitId, _, requisitionId) = await SeedRequisitionAsync(admin);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var watcher = await SseConnection.OpenAsync(admin, cts.Token);

        // O admin entra na presença para ser "quem está vendo o recurso" — e drena o sinal que a
        // própria entrada dispara, para não confundi-lo com o que viria (ou não) do leave abaixo.
        var enter = await admin.PostAsJsonAsync("/api/realtime/presence",
            new PresenceRequest(watcher.ConnectionId.ToString(), "requisition", requisitionId));
        Assert.Equal(HttpStatusCode.NoContent, enter.StatusCode);
        var enterSignal = await watcher.TryReadNextSignalAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(enterSignal);

        // Um Requester fora do escopo (não consegue ver a requisição) tenta sair da presença dela —
        // a remoção em si é sempre permitida (idempotente), só a publicação do sinal deve ser recusada.
        var outsider = factory.CreateClientWithUnitScope(
            Roles.Requester, [companyId], [(companyId, unitId)], [(companyId, Guid.NewGuid())],
            Permissions.RequisitionsRead);
        var leave = await outsider.DeleteAsync(
            $"/api/realtime/presence?connectionId={Guid.NewGuid()}&scope=requisition&resourceId={requisitionId}&seq=1");
        Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);

        var leakedSignal = await watcher.TryReadNextSignalAsync(TimeSpan.FromSeconds(3));
        Assert.Null(leakedSignal);
    }
}
