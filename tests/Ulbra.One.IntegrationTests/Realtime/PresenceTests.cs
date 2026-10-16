using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Identity.Domain;
using Ulbra.One.IntegrationTests.Finance;

namespace Ulbra.One.IntegrationTests.Realtime;

[Collection("Postgres")]
public class PresenceTests(PostgresFixture fixture)
{
    private sealed record PresenceRequest(string ConnectionId, string Scope, Guid ResourceId);

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

        var users = await client.GetFromJsonAsync<Guid[]>(
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

        var users = await outsider.GetFromJsonAsync<Guid[]>(
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

        var res = await insider.PostAsJsonAsync("/api/realtime/presence",
            new PresenceRequest(Guid.NewGuid().ToString(), "requisition", requisitionId));

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
    }

    [Fact]
    public async Task Sair_exige_autenticacao()
    {
        var factory = fixture.Api;
        var client = factory.CreateClient();

        var res = await client.DeleteAsync(
            $"/api/realtime/presence?connectionId={Guid.NewGuid()}&scope=requisition&resourceId={Guid.NewGuid()}");

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
            $"/api/realtime/presence?connectionId={Guid.NewGuid()}&scope=requisition&resourceId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
    }

    [Fact]
    public async Task Entrar_e_sair_completa_o_ciclo_de_presenca()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var (_, _, _, requisitionId) = await SeedRequisitionAsync(client);
        var connectionId = Guid.NewGuid();

        var enter = await client.PostAsJsonAsync("/api/realtime/presence",
            new PresenceRequest(connectionId.ToString(), "requisition", requisitionId));
        Assert.Equal(HttpStatusCode.NoContent, enter.StatusCode);

        var afterEnter = await client.GetFromJsonAsync<Guid[]>(
            $"/api/realtime/presence?scope=requisition&resourceId={requisitionId}");
        Assert.Single(afterEnter!);

        var leave = await client.DeleteAsync(
            $"/api/realtime/presence?connectionId={connectionId}&scope=requisition&resourceId={requisitionId}");
        Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);

        var afterLeave = await client.GetFromJsonAsync<Guid[]>(
            $"/api/realtime/presence?scope=requisition&resourceId={requisitionId}");
        Assert.Empty(afterLeave!);
    }
}
