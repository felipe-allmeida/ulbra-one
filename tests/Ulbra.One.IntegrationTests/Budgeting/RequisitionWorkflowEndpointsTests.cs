using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Identity.Domain;
using Ulbra.One.IntegrationTests.Finance;
using Xunit;

namespace Ulbra.One.IntegrationTests.Budgeting;

/// <summary>Approval-workflow (esteira) endpoints — AEL-789, remapped in AEL-841 to
/// Draft → Triage → FinancialComplement → Approval → InPayment. No shortcuts (autonomy / cost-center
/// auto-approval) and no alçada; a budget breach is a hard 409 resolved by an Adequação Orçamentária.</summary>
[Collection("Postgres")]
public class RequisitionWorkflowEndpointsTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    /// <summary>The fatura do mês is mandatory on a Conta Fixa cycle (AEL-959) — a valid PDF part for the launch.</summary>
    private static ByteArrayContent FaturaPart()
    {
        var pdf = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.4 fatura do mes"));
        pdf.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        return pdf;
    }

    /// <summary>Asserts a 2xx and returns the parsed body; on failure the message carries status + body
    /// so a Problem response (title/detail) is visible instead of a downstream JSON-shape error.</summary>
    private static async Task<JsonElement> Ok(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static string UniqueEmail() => $"boss_{Guid.NewGuid():N}@ulbra.br";

    private static string Status(JsonElement req) => req.GetProperty("status").GetString()!;

    /// <summary>Admin client + the persisted user's typed id (from /auth/me).</summary>
    private static async Task<(HttpClient Client, string UserId)> AdminAsync(ApiFactory factory)
    {
        var client = await factory.CreateAdminClientAsync(UniqueEmail());
        var me = await Ok(await client.GetAsync("/api/auth/me"));
        return (client, me.GetProperty("userId").GetString()!);
    }

    /// <summary>Seeds company + unit + a cost center. <paramref name="seedBudget"/> false leaves the
    /// competency without a budget cell so the approval hits the authoritative budget guard (409).</summary>
    private static async Task<(string CompanyId, string UnitId, string CostCenterId, string VendorId)> SeedDimensionAsync(
        HttpClient client, bool seedBudget = true)
    {
        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var unitId = await FinanceSeed.CreateUnitAsync(client, companyId, "Campus Teste", "CT");
        var ccRes = await client.PostAsJsonAsync("/api/cost-centers", new
        {
            companyId,
            code = $"CC{Random.Shared.Next(100_000, 999_999)}",
            name = "Marketing",
            autoApprovalLimit = 0m,
            unitIds = new[] { unitId }, // unit must be associated with the cost center (required by requisition create)
        });
        var cc = await Ok(ccRes);
        var costCenterId = cc.GetProperty("id").GetString()!;
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Teste");
        // Orçamento generoso para a competência dos testes (2026-07): sem célula, o guard de orçamento
        // (AEL-796: CC sem orçamento na competência bloqueia) travaria a aprovação. Cobre 2026-07..2026-12.
        // `seedBudget: false` para testar justamente o bloqueio.
        if (seedBudget)
            await Ok(await client.PostAsJsonAsync($"/api/cost-centers/{costCenterId}/unit-budgets", new
            {
                unitId,
                startCompetency = "2026-07",
                endCompetency = "2026-12",
                monthlyLimit = 1_000_000m,
            }));
        return (companyId, unitId, costCenterId, vendorId);
    }

    private static async Task<string> CreateAsync(HttpClient client, string unitId, string costCenterId, string vendorId, decimal estimate)
    {
        var res = await client.PostAsJsonAsync("/api/requisitions", new
        {
            unitId,
            costCenterId,
            vendorId,
            type = "Purchase",
            description = "Comprar cadeiras",
            observation = (string?)null,
            estimateValue = estimate,
            estimateType = "Full",
            startCompetency = "2026-07",
            endCompetency = (string?)null,
            firstDueDate = "2026-07-10",
            paymentFrequency = (string?)null,
            paymentInstallmentsCount = (int?)null,
        });
        return (await Ok(res)).GetProperty("id").GetString()!;
    }

    /// <summary>Creates a requisition already carrying the front-loaded financial plan (Onda 2): emission date +
    /// authored installments, so triage approval skips the financial-complement step.</summary>
    private static async Task<string> CreateWithPlanAsync(
        HttpClient client, string unitId, string costCenterId, string vendorId, decimal estimate,
        object[] installments, string issueDate = "2026-07-05")
    {
        var res = await client.PostAsJsonAsync("/api/requisitions", new
        {
            unitId,
            costCenterId,
            vendorId,
            type = "Purchase",
            description = "Comprar cadeiras",
            observation = (string?)null,
            estimateValue = estimate,
            estimateType = "Full",
            startCompetency = "2026-07",
            endCompetency = (string?)null,
            firstDueDate = "2026-07-10",
            paymentFrequency = (string?)null,
            paymentInstallmentsCount = (int?)null,
            issueDate,
            installments,
        });
        return (await Ok(res)).GetProperty("id").GetString()!;
    }

    private static Task<JsonElement> PostAsync(HttpClient client, string id, string action) =>
        Ok2(client.PostAsJsonAsync($"/api/requisitions/{id}/{action}", new { }));

    /// <summary>Submits the financial complement with a single authored installment for the given total
    /// (AEL-844) — the endpoint requires the emission date + at least one parcela.</summary>
    private static Task<JsonElement> SubmitFcAsync(HttpClient client, string id, decimal total = 1_000m) =>
        Ok2(client.PostAsJsonAsync($"/api/requisitions/{id}/submit-financial-complement", new
        {
            issueDate = "2026-07-05",
            installments = new[] { new { dueDate = "2026-07-10", amount = total, paymentMethod = "Pix" } },
        }));

    private static async Task<JsonElement> Ok2(Task<HttpResponseMessage> call) => await Ok(await call);

    // ---- Happy-path mill -------------------------------------------------------------------------

    [Fact]
    public async Task Submit_ApproveTriage_Complement_Approve_Flow()
    {
        var factory = fixture.Api;
        var (client, _) = await AdminAsync(factory);
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionAsync(client);
        var id = await CreateAsync(client, unitId, costCenterId, vendorId, 1_000m);

        Assert.Equal("Triage", Status(await PostAsync(client, id, "submit")));
        Assert.Equal("FinancialComplement", Status(await PostAsync(client, id, "approve-triage")));
        Assert.Equal("Approval", Status(await SubmitFcAsync(client, id)));
        var approved = await PostAsync(client, id, "approve");
        Assert.Equal("InPayment", Status(approved));

        var actions = approved.GetProperty("transitions").EnumerateArray().Select(t => t.GetProperty("action").GetString()).ToList();
        Assert.Equal(new[] { "Submit", "ApproveTriage", "SubmitFinancialComplement", "Approve" }, actions);
    }

    // ---- Front-load da parte financeira (Onda 2) -------------------------------------------------

    [Fact]
    public async Task FrontLoaded_Create_ApproveTriage_goes_straight_to_approval()
    {
        var factory = fixture.Api;
        var (client, _) = await AdminAsync(factory);
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionAsync(client);
        var id = await CreateWithPlanAsync(client, unitId, costCenterId, vendorId, 1_000m,
            new object[] { new { dueDate = "2026-07-10", amount = 1_000m, paymentMethod = "Pix" } });

        Assert.Equal("Triage", Status(await PostAsync(client, id, "submit")));
        // Plano completo → salta o FinancialComplement e vai direto para Approval.
        Assert.Equal("Approval", Status(await PostAsync(client, id, "approve-triage")));
        var approved = await PostAsync(client, id, "approve");
        Assert.Equal("InPayment", Status(approved));

        var actions = approved.GetProperty("transitions").EnumerateArray().Select(t => t.GetProperty("action").GetString()).ToList();
        Assert.Equal(new[] { "Submit", "ApproveTriage", "Approve" }, actions);
    }

    [Fact]
    public async Task FrontLoaded_plan_persisted_on_create()
    {
        var factory = fixture.Api;
        var (client, _) = await AdminAsync(factory);
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionAsync(client);
        var id = await CreateWithPlanAsync(client, unitId, costCenterId, vendorId, 500m, new object[]
        {
            new { dueDate = "2026-07-10", amount = 200m, paymentMethod = "Pix" },
            new { dueDate = "2026-08-10", amount = 300m, paymentMethod = "Pix" },
        });

        var req = await Ok2(client.GetAsync($"/api/requisitions/{id}"));
        Assert.Equal("2026-07-05", req.GetProperty("issueDate").GetString());
        Assert.Equal(2, req.GetProperty("installments").GetArrayLength());
        Assert.Equal(500m, req.GetProperty("estimateValue").GetDecimal()); // Σ parcelas
    }

    [Fact]
    public async Task Update_replaces_front_loaded_plan()
    {
        var factory = fixture.Api;
        var (client, _) = await AdminAsync(factory);
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionAsync(client);
        var id = await CreateWithPlanAsync(client, unitId, costCenterId, vendorId, 500m,
            new object[] { new { dueDate = "2026-07-10", amount = 500m, paymentMethod = "Pix" } });

        var put = await Ok2(client.PutAsJsonAsync($"/api/requisitions/{id}", new
        {
            type = "Purchase",
            description = "Comprar cadeiras",
            observation = (string?)null,
            estimateValue = 500m,
            estimateType = "Full",
            startCompetency = "2026-07",
            endCompetency = (string?)null,
            vendorId,
            firstDueDate = "2026-07-10",
            paymentFrequency = (string?)null,
            paymentInstallmentsCount = (int?)null,
            projectId = (string?)null,
            issueDate = "2026-07-06",
            installments = new object[]
            {
                new { dueDate = "2026-07-10", amount = 100m, paymentMethod = "Pix" },
                new { dueDate = "2026-08-10", amount = 250m, paymentMethod = "Pix" },
            },
        }));

        Assert.Equal(2, put.GetProperty("installments").GetArrayLength());
        Assert.Equal(350m, put.GetProperty("estimateValue").GetDecimal()); // novo Σ parcelas
        Assert.Equal("2026-07-06", put.GetProperty("issueDate").GetString());
    }

    [Fact]
    public async Task Update_clears_plan_when_absent_and_classic_path_resumes()
    {
        var factory = fixture.Api;
        var (client, _) = await AdminAsync(factory);
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionAsync(client);
        var id = await CreateWithPlanAsync(client, unitId, costCenterId, vendorId, 500m,
            new object[] { new { dueDate = "2026-07-10", amount = 500m, paymentMethod = "Pix" } });

        // PUT sem issueDate/installments → limpa o plano front-loaded.
        var put = await Ok2(client.PutAsJsonAsync($"/api/requisitions/{id}", new
        {
            type = "Purchase",
            description = "Comprar cadeiras",
            observation = (string?)null,
            estimateValue = 500m,
            estimateType = "Full",
            startCompetency = "2026-07",
            endCompetency = (string?)null,
            vendorId,
            firstDueDate = "2026-07-10",
            paymentFrequency = (string?)null,
            paymentInstallmentsCount = (int?)null,
            projectId = (string?)null,
        }));
        Assert.Equal(0, put.GetProperty("installments").GetArrayLength());

        // Sem plano → o caminho clássico volta (approve-triage para o FinancialComplement).
        Assert.Equal("Triage", Status(await PostAsync(client, id, "submit")));
        Assert.Equal("FinancialComplement", Status(await PostAsync(client, id, "approve-triage")));
    }

    [Fact]
    public async Task ReturnToFinancialComplement_from_approval()
    {
        var factory = fixture.Api;
        var (client, _) = await AdminAsync(factory);
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionAsync(client);
        var id = await CreateWithPlanAsync(client, unitId, costCenterId, vendorId, 1_000m,
            new object[] { new { dueDate = "2026-07-10", amount = 1_000m, paymentMethod = "Pix" } });

        await PostAsync(client, id, "submit");
        Assert.Equal("Approval", Status(await PostAsync(client, id, "approve-triage")));

        var returned = await Ok2(client.PostAsJsonAsync($"/api/requisitions/{id}/return-to-financial-complement",
            new { reason = "ajustar a data de vencimento" }));
        Assert.Equal("FinancialComplement", Status(returned));
    }

    // A draft saves with a malformed boleto line (read PDF still attaches); the 47/48 rule fires only at submit.
    [Fact]
    public async Task FrontLoaded_boleto_invalid_saves_as_draft_but_blocks_submit()
    {
        var factory = fixture.Api;
        var (client, _) = await AdminAsync(factory);
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionAsync(client);

        var res = await client.PostAsJsonAsync("/api/requisitions", new
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
            issueDate = "2026-07-05",
            installments = new object[]
            {
                new { dueDate = "2026-07-10", amount = 1_000m, paymentMethod = "Boleto", boletoDigitableLine = "123" },
            },
        });

        // Create succeeds — the draft keeps the malformed line.
        res.EnsureSuccessStatusCode();
        var id = (await ReadJson(res)).GetProperty("id").GetString();

        // Submit to triage is rejected until the line is valid; the transition guard maps to 409 (InvalidState).
        var submit = await client.PostAsync($"/api/requisitions/{id}/submit", null);
        Assert.Equal(HttpStatusCode.Conflict, submit.StatusCode);
        Assert.Equal(ErrorCodes.Requisition.BoletoDigitableLineInvalid, (await ReadJson(submit)).GetProperty("error_code").GetString());
    }

    // ---- Recurrence cycles (Conta Fixa / Recorrência), AEL-877 -----------------------------------

    [Fact]
    public async Task Recurrence_launch_cycle_duplicate_blocked_and_terminate_via_endpoints()
    {
        var factory = fixture.Api;
        var (client, _) = await AdminAsync(factory);
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionAsync(client);

        // A recurring requisition (Conta Fixa), closed window within the budget, driven to InPayment.
        var id = (await Ok(await client.PostAsJsonAsync("/api/requisitions", new
        {
            unitId, costCenterId, vendorId,
            type = "FixedExpense", description = "Conta de luz", estimateValue = 1_000m,
            estimateType = "Monthly", startCompetency = "2026-07", endCompetency = "2026-12", firstDueDate = "2026-07-10",
        }))).GetProperty("id").GetString()!;
        await PostAsync(client, id, "submit");
        await PostAsync(client, id, "approve-triage");
        await SubmitFcAsync(client, id);
        Assert.Equal("InPayment", Status(await PostAsync(client, id, "approve")));

        // AEL-959: the fatura do mês is mandatory — a launch without a file is rejected before anything persists.
        var noFile = await client.PostAsync($"/api/requisitions/{id}/cycles", new MultipartFormDataContent
        {
            { new StringContent("2026-08"), "competency" },
            { new StringContent("2026-08-10"), "dueDate" },
            { new StringContent("900"), "amount" },
            { new StringContent("Pix"), "paymentMethod" },
        });
        Assert.Equal(HttpStatusCode.BadRequest, noFile.StatusCode);
        Assert.Equal(ErrorCodes.Requisition.CycleInvoiceRequired, (await ReadJson(noFile)).GetProperty("error_code").GetString());

        // Launch the August invoice, paid by boleto (convênio, 48 digits) — 200 with the cycle listed.
        var afterCycle = await Ok2(client.PostAsync($"/api/requisitions/{id}/cycles", new MultipartFormDataContent
        {
            { new StringContent("2026-08"), "competency" },
            { new StringContent("2026-08-10"), "dueDate" },
            { new StringContent("900"), "amount" },
            { new StringContent("Boleto"), "paymentMethod" },
            { new StringContent("846700000017599200241114202410000000000000000000"), "boletoDigitableLine" },
            { FaturaPart(), "file", "fatura-agosto.pdf" },
        }));
        var cycle = Assert.Single(afterCycle.GetProperty("cycles").EnumerateArray());
        Assert.Equal("2026-08", cycle.GetProperty("competency").GetString());
        Assert.Equal("InPayment", Status(afterCycle)); // still open

        // Same competency again → 409 (one cycle per competency).
        var dup = await client.PostAsync($"/api/requisitions/{id}/cycles", new MultipartFormDataContent
        {
            { new StringContent("2026-08"), "competency" },
            { new StringContent("2026-08-15"), "dueDate" },
            { new StringContent("500"), "amount" },
            { new StringContent("Pix"), "paymentMethod" },
            { FaturaPart(), "file", "fatura-agosto-2.pdf" },
        });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);

        // Terminate the recurrence at 2026-08 → end competency fixed (stays InPayment until the cycles are paid).
        var terminated = await Ok2(client.PostAsJsonAsync($"/api/requisitions/{id}/terminate-recurrence", new { endCompetency = "2026-08" }));
        Assert.Equal("2026-08", terminated.GetProperty("endCompetency").GetString());
    }

    // ---- Budget guard: CC sem orçamento na competência bloqueia a aprovação (AEL-796, server-side) ----

    [Fact]
    public async Task Approve_Without_Budget_Cell_Is_Blocked_409()
    {
        var factory = fixture.Api;
        var (client, _) = await AdminAsync(factory);
        // Dimensão SEM célula de orçamento na competência do pedido (seedBudget: false).
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionAsync(client, seedBudget: false);
        var id = await CreateAsync(client, unitId, costCenterId, vendorId, 1_000m);
        await PostAsync(client, id, "submit");
        await PostAsync(client, id, "approve-triage");
        await SubmitFcAsync(client, id);

        // Sem orçamento → 409 BudgetExceeded (guard autoritativo do servidor; sem override — AEL-841).
        var res = await client.PostAsJsonAsync($"/api/requisitions/{id}/approve", new { });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal(ErrorCodes.Requisition.BudgetExceeded, (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task FixedExpense_Bypasses_Budget_Ceiling_While_A_Regular_Requisition_Does_Not()
    {
        // AEL-881 (R1): Conta Fixa (valor variável) não trava por orçamento; os demais tipos mantêm o teto duro.
        var factory = fixture.Api;
        var (client, _) = await AdminAsync(factory);
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionAsync(client, seedBudget: false); // sem célula

        async Task<HttpResponseMessage> DriveToApprove(object body)
        {
            var id = (await Ok(await client.PostAsJsonAsync("/api/requisitions", body))).GetProperty("id").GetString()!;
            await PostAsync(client, id, "submit");
            await PostAsync(client, id, "approve-triage");
            await SubmitFcAsync(client, id);
            return await client.PostAsJsonAsync($"/api/requisitions/{id}/approve", new { });
        }

        // Conta Fixa: sem teto → aprova mesmo sem célula de orçamento.
        Assert.Equal("InPayment", Status(await Ok(await DriveToApprove(new
        {
            unitId, costCenterId, vendorId, type = "FixedExpense", description = "Conta de luz", estimateValue = 1_000m,
            estimateType = "Monthly", startCompetency = "2026-07", endCompetency = "2026-12", firstDueDate = "2026-07-10",
        }))));

        // Requisição comum (compra à vista): mantém o teto → 409 BudgetExceeded na mesma situação (diferença provada).
        var regular = await DriveToApprove(new
        {
            unitId, costCenterId, vendorId, type = "Purchase", description = "Compra pontual", estimateValue = 1_000m,
            estimateType = "Full", startCompetency = "2026-07", firstDueDate = "2026-07-10",
        });
        Assert.Equal(HttpStatusCode.Conflict, regular.StatusCode);
        Assert.Equal(ErrorCodes.Requisition.BudgetExceeded, (await ReadJson(regular)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task FixedExpense_BudgetControl_Still_Reports_Breach_For_Visualization()
    {
        // AEL-881 (R1): a Conta Fixa PARTICIPA do orçamento — o read-model (`/budget-control`) segue reportando o
        // estouro para o painel/gráfico mostrarem o impacto; o valor entra no consumido. A única diferença fica no
        // ENDPOINT `approve` (que a isenta do bloqueio), coberta por FixedExpense_Bypasses_Budget_Ceiling_*.
        var factory = fixture.Api;
        var (client, _) = await AdminAsync(factory);
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionAsync(client, seedBudget: false); // sem célula

        var id = (await Ok(await client.PostAsJsonAsync("/api/requisitions", new
        {
            unitId, costCenterId, vendorId, type = "FixedExpense", description = "Conta de luz", estimateValue = 1_000m,
            estimateType = "Monthly", startCompetency = "2026-07", endCompetency = "2026-12", firstDueDate = "2026-07-10",
        }))).GetProperty("id").GetString()!;

        var ctl = await Ok(await client.GetAsync($"/api/requisitions/{id}/budget-control"));
        // O estouro CONTINUA visível (não suprimimos a visualização) — a isenção é só do bloqueio de aprovação.
        Assert.NotEmpty(ctl.GetProperty("breaches").EnumerateArray());
        Assert.NotEmpty(ctl.GetProperty("months").EnumerateArray());
    }

    [Fact]
    public async Task FixedExpense_Is_Unique_Per_Vendor_Unit_CostCenter_While_Active()
    {
        // AEL-881 (R2): não pode haver duas "contas de luz" ativas do mesmo (fornecedor, unidade, natureza=CC).
        var factory = fixture.Api;
        var (client, _) = await AdminAsync(factory);
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionAsync(client);

        Task<HttpResponseMessage> CreateFixed() => client.PostAsJsonAsync("/api/requisitions", new
        {
            unitId, costCenterId, vendorId, type = "FixedExpense", description = "Conta de luz", estimateValue = 1_000m,
            estimateType = "Monthly", startCompetency = "2026-07", endCompetency = "2026-12", firstDueDate = "2026-07-10",
        });

        var first = await CreateFixed();
        var firstId = (await Ok(first)).GetProperty("id").GetString()!;

        // Segunda Conta Fixa do mesmo trio, ainda ativa → 409 FixedExpenseDuplicate.
        var dup = await CreateFixed();
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        Assert.Equal(ErrorCodes.Requisition.FixedExpenseDuplicate, (await ReadJson(dup)).GetProperty("error_code").GetString());

        // Uma requisição de outro tipo (compra) do mesmo trio NÃO colide (a regra de unicidade é só da Conta Fixa).
        var other = await client.PostAsJsonAsync("/api/requisitions", new
        {
            unitId, costCenterId, vendorId, type = "Purchase", description = "Compra pontual", estimateValue = 500m,
            estimateType = "Full", startCompetency = "2026-07", firstDueDate = "2026-07-10",
        });
        Assert.True(other.IsSuccessStatusCode, $"Outro tipo não deveria colidir: {(int)other.StatusCode}");

        // Cancelada a primeira, a chave é liberada → nova Conta Fixa do mesmo trio é aceita.
        await PostAsync(client, firstId, "submit");
        await Ok2(client.PostAsJsonAsync($"/api/requisitions/{firstId}/cancel", new { reason = "trocou de fornecedor" }));
        var reopened = await CreateFixed();
        Assert.True(reopened.IsSuccessStatusCode, $"Após cancelar a 1ª deveria permitir: {(int)reopened.StatusCode}");
    }

    // ---- Permission gating -----------------------------------------------------------------------

    [Fact]
    public async Task Approve_Without_Approve_Permission_Returns_403()
    {
        var factory = fixture.Api;
        var client = factory.CreateClientWith(Roles.Finance, Permissions.RequisitionsWrite);
        var res = await client.PostAsJsonAsync($"/api/requisitions/req_{Guid.NewGuid():N}/approve", new { });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task ApproveTriage_Without_Triage_Permission_Returns_403()
    {
        var factory = fixture.Api;
        var client = factory.CreateClientWith(Roles.Finance, Permissions.RequisitionsWrite);
        var res = await client.PostAsJsonAsync($"/api/requisitions/req_{Guid.NewGuid():N}/approve-triage", new { });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Submit_Anonymous_Returns_401()
    {
        var factory = fixture.Api;
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync($"/api/requisitions/req_{Guid.NewGuid():N}/submit", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    // ---- Detail exposes the authored installment plan (AEL-859) -----------------------------------

    [Fact]
    public async Task Get_Detail_Returns_Authored_Installments()
    {
        var factory = fixture.Api;
        var (client, _) = await AdminAsync(factory);
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionAsync(client);
        var id = await CreateAsync(client, unitId, costCenterId, vendorId, 900m);
        await PostAsync(client, id, "submit");
        await PostAsync(client, id, "approve-triage");
        await Ok2(client.PostAsJsonAsync($"/api/requisitions/{id}/submit-financial-complement", new
        {
            issueDate = "2026-07-05",
            installments = new[]
            {
                new { dueDate = "2026-07-10", amount = 300m, paymentMethod = "Pix", boletoDigitableLine = (string?)null },
                new { dueDate = "2026-08-10", amount = 300m, paymentMethod = "Boleto", boletoDigitableLine = (string?)"34191790010104351004791020150008291070026000123" },
                new { dueDate = "2026-09-10", amount = 300m, paymentMethod = "Pix", boletoDigitableLine = (string?)null },
            },
        }));

        var fetched = await Ok(await client.GetAsync($"/api/requisitions/{id}"));
        var installments = fetched.GetProperty("installments").EnumerateArray().ToList();
        Assert.Equal(3, installments.Count);
        Assert.Equal(new[] { "2026-07-10", "2026-08-10", "2026-09-10" },
            installments.Select(i => i.GetProperty("dueDate").GetString()).ToArray());
        Assert.All(installments, i => Assert.Equal(300m, i.GetProperty("amount").GetDecimal()));
        Assert.Equal("Boleto", installments[1].GetProperty("paymentMethod").GetString());
    }

    [Fact]
    public async Task SubmitFinancialComplement_Boleto_Without_Line_Returns_400()
    {
        var factory = fixture.Api;
        var (client, _) = await AdminAsync(factory);
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionAsync(client);
        var id = await CreateAsync(client, unitId, costCenterId, vendorId, 300m);
        await PostAsync(client, id, "submit");
        await PostAsync(client, id, "approve-triage");

        // A boleto installment must carry its linha digitável at entry (AEL-932) — a blank one is rejected.
        var res = await client.PostAsJsonAsync($"/api/requisitions/{id}/submit-financial-complement", new
        {
            issueDate = "2026-07-05",
            installments = new[]
            {
                new { dueDate = "2026-08-10", amount = 300m, paymentMethod = "Boleto", boletoDigitableLine = (string?)null },
            },
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(ErrorCodes.Requisition.BoletoDigitableLineInvalid,
            JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("error_code").GetString());
    }

    // ---- Return for complement persists reason + history -----------------------------------------

    [Fact]
    public async Task Return_For_Complement_Persists_Reason_And_History()
    {
        var factory = fixture.Api;
        var (client, _) = await AdminAsync(factory);
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionAsync(client);

        var id = await CreateAsync(client, unitId, costCenterId, vendorId, 1_000m);
        await PostAsync(client, id, "submit");
        await PostAsync(client, id, "approve-triage");
        await SubmitFcAsync(client, id); // now in Approval

        var returnRes = await client.PostAsJsonAsync($"/api/requisitions/{id}/return-for-complement", new { reason = "faltou orçamento" });
        Assert.Equal("Complement", Status(await Ok(returnRes)));

        var fetched = await Ok(await client.GetAsync($"/api/requisitions/{id}"));
        var last = fetched.GetProperty("transitions").EnumerateArray().Last();
        Assert.Equal("ReturnForComplement", last.GetProperty("action").GetString());
        Assert.Equal("faltou orçamento", last.GetProperty("reason").GetString());
        Assert.Equal("Approval", last.GetProperty("fromStatus").GetString());
        Assert.Equal("Complement", last.GetProperty("toStatus").GetString());
    }
}
