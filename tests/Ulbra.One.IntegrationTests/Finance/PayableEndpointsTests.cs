using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ulbra.One.IntegrationTests.Finance;

[Collection("Postgres")]
public class PayableEndpointsTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private sealed record Seed(string CompanyId, string CostCenterId, string VendorId);

    /// <summary>Seeds a company, a cost center (with the given auto-approval limit) and a vendor via the public API.</summary>
    private static async Task<Seed> SeedAsync(HttpClient client, decimal autoApprovalLimit = 0)
    {
        // Company CNPJ is stored as-is (max 14 chars) and is globally unique → use a fresh 14-digit value per seed.
        var cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        var companyRes = await client.PostAsJsonAsync("/api/companies",
            new { legalName = "ULBRA Teste", cnpj });
        companyRes.EnsureSuccessStatusCode();
        var companyId = (await ReadJson(companyRes)).GetProperty("id").GetString()!;

        var ccRes = await client.PostAsJsonAsync("/api/cost-centers",
            new { companyId, code = "CC1", name = "Marketing", annualBudget = 1_000_000m, autoApprovalLimit, unitIds = Array.Empty<string>() });
        ccRes.EnsureSuccessStatusCode();
        var costCenterId = (await ReadJson(ccRes)).GetProperty("id").GetString()!;

        var vendorRes = await client.PostAsJsonAsync("/api/vendors",
            new
            {
                companyId,
                personType = 1,
                name = "Fornecedor Sul Ltda",
                tradeName = "Fornecedor Sul",
                document = "11.222.333/0001-81",
                email = "contato@fs.com",
                phone = "5133334444",
                address = (object?)null,
                paymentTerms = "30 dias",
                bankInfo = (string?)null,
            });
        vendorRes.EnsureSuccessStatusCode();
        var vendorId = (await ReadJson(vendorRes)).GetProperty("id").GetString()!;

        return new Seed(companyId, costCenterId, vendorId);
    }

    [Fact]
    public async Task Anonymous_Returns_401()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = factory.CreateClient();
        var res = await client.GetAsync("/api/payables");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Create_Parcelado_Generate_Returns_201_With_Three_Installments()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);

        var res = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Compra parcelada",
            generate = new { totalAmount = 90000m, count = 3, firstDueDate = "2026-07-10", intervalMonths = 1 },
        });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var body = await ReadJson(res);
        Assert.Equal(90000m, body.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(3, body.GetProperty("installments").GetArrayLength());
    }

    [Fact]
    public async Task Create_AVista_Explicit_Single_Installment_Returns_201()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);

        var res = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Compra à vista",
            installments = new[] { new { dueDate = "2026-07-10", amount = 1500m } },
        });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var body = await ReadJson(res);
        Assert.Equal(1500m, body.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(1, body.GetProperty("installments").GetArrayLength());
    }

    [Fact]
    public async Task Create_Invalid_Vendor_Returns_400_VendorInvalid()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);

        var res = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = $"vendor_{Guid.NewGuid():N}",
            costCenterId = seed.CostCenterId,
            description = "Fornecedor inválido",
            installments = new[] { new { dueDate = "2026-07-10", amount = 100m } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Payable.VendorInvalid", (await ReadJson(res)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Create_Without_Installments_Or_Generate_Returns_400_InstallmentsInvalid()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);

        var res = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Sem parcelas",
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Payable.InstallmentsInvalid", (await ReadJson(res)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Create_Always_Returns_Received_Status()
    {
        // Since SP-2, auto-approval is handled asynchronously by PayableReceivedConsumer.
        // The create endpoint always returns Received; see PayableAutoClassifyTests for
        // the consumer-driven approval flow.
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client, autoApprovalLimit: 5000m);

        var within = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Dentro do limite",
            installments = new[] { new { dueDate = "2026-07-10", amount = 1000m } },
        });
        Assert.Equal(HttpStatusCode.Created, within.StatusCode);
        Assert.Equal("Received", (await ReadJson(within)).GetProperty("status").GetString());

        var above = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Acima do limite",
            installments = new[] { new { dueDate = "2026-07-10", amount = 9000m } },
        });
        Assert.Equal(HttpStatusCode.Created, above.StatusCode);
        Assert.Equal("Received", (await ReadJson(above)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task List_Returns_One_Row_Per_Installment_Ordered_By_DueDate()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);

        var createRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Parcelado para listar",
            generate = new { totalAmount = 90000m, count = 3, firstDueDate = "2026-07-10", intervalMonths = 1 },
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var payableId = (await ReadJson(createRes)).GetProperty("id").GetString()!;

        var listRes = await client.GetAsync($"/api/payables?companyId={seed.CompanyId}&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var list = await ReadJson(listRes);

        var rows = list.GetProperty("items").EnumerateArray()
            .Where(r => r.GetProperty("payableId").GetString() == payableId)
            .ToList();
        Assert.Equal(3, rows.Count);

        // Each row exposes its sequence + the total installment count, ordered by due date.
        var dueDates = rows.Select(r => r.GetProperty("dueDate").GetString()!).ToList();
        Assert.Equal(dueDates.OrderBy(d => d).ToList(), dueDates);
        Assert.All(rows, r => Assert.Equal(3, r.GetProperty("installmentCount").GetInt32()));
        Assert.Equal(new[] { 1, 2, 3 }, rows.Select(r => r.GetProperty("sequence").GetInt32()).ToArray());
    }

    [Fact]
    public async Task Get_Returns_Payable_With_Installments()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);

        var createRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Para buscar",
            generate = new { totalAmount = 60000m, count = 2, firstDueDate = "2026-07-10", intervalMonths = 1 },
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var payableId = (await ReadJson(createRes)).GetProperty("id").GetString()!;

        var getRes = await client.GetAsync($"/api/payables/{payableId}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var body = await ReadJson(getRes);
        Assert.Equal(payableId, body.GetProperty("id").GetString());
        Assert.Equal(2, body.GetProperty("installments").GetArrayLength());

        var missing = await client.GetAsync($"/api/payables/payable_{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    // ---------------------------------------------------------------------------
    // Approve
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Approve_PendingPayable_Returns_200_Approved()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);

        var createRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Para aprovar",
            installments = new[] { new { dueDate = "2026-07-10", amount = 5000m } },
            isService = false, // determined product — required before a payable can schedule
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var payableId = (await ReadJson(createRes)).GetProperty("id").GetString()!;

        var approveRes = await client.PostAsJsonAsync($"/api/payables/{payableId}/approve", new { });
        Assert.Equal(HttpStatusCode.OK, approveRes.StatusCode);
        var body = await ReadJson(approveRes);
        Assert.Equal("Scheduled", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Approve_Records_ApprovedBy_And_ApprovedAt()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);

        var createRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Auditoria de aprovação",
            installments = new[] { new { dueDate = "2026-07-10", amount = 5000m } },
            isService = false, // determined product — required before a payable can schedule
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var payableId = (await ReadJson(createRes)).GetProperty("id").GetString()!;

        var approveRes = await client.PostAsJsonAsync($"/api/payables/{payableId}/approve", new { });
        Assert.Equal(HttpStatusCode.OK, approveRes.StatusCode);

        var getRes = await client.GetAsync($"/api/payables/{payableId}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var body = await ReadJson(getRes);
        Assert.Equal("Scheduled", body.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("approvedBy").GetString()));
        Assert.NotEqual(default, body.GetProperty("approvedAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("rejectedBy").ValueKind);
    }

    [Fact]
    public async Task Reject_Records_RejectedBy_And_RejectedAt()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);

        var createRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Auditoria de rejeição",
            installments = new[] { new { dueDate = "2026-07-10", amount = 3000m } },
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var payableId = (await ReadJson(createRes)).GetProperty("id").GetString()!;

        var rejectRes = await client.PostAsJsonAsync($"/api/payables/{payableId}/reject", new { reason = "Fora do orçamento" });
        Assert.Equal(HttpStatusCode.OK, rejectRes.StatusCode);

        var getRes = await client.GetAsync($"/api/payables/{payableId}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var body = await ReadJson(getRes);
        Assert.Equal("Rejected", body.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("rejectedBy").GetString()));
        Assert.NotEqual(default, body.GetProperty("rejectedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Approve_NonExistent_Returns_404_NotFound()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        var res = await client.PostAsJsonAsync($"/api/payables/payable_{Guid.NewGuid():N}/approve", new { });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("Payable.NotFound", (await ReadJson(res)).GetProperty("title").GetString());
    }

    // ---------------------------------------------------------------------------
    // Reject
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Reject_PendingPayable_Returns_200_Rejected_With_Reason()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);

        var createRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Para rejeitar",
            installments = new[] { new { dueDate = "2026-07-10", amount = 3000m } },
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var payableId = (await ReadJson(createRes)).GetProperty("id").GetString()!;

        var rejectRes = await client.PostAsJsonAsync($"/api/payables/{payableId}/reject", new { reason = "Fora do orçamento" });
        Assert.Equal(HttpStatusCode.OK, rejectRes.StatusCode);
        var body = await ReadJson(rejectRes);
        Assert.Equal("Rejected", body.GetProperty("status").GetString());
        Assert.Equal("Fora do orçamento", body.GetProperty("rejectionReason").GetString());
    }

    [Fact]
    public async Task Reject_NonExistent_Returns_404_NotFound()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        var res = await client.PostAsJsonAsync($"/api/payables/payable_{Guid.NewGuid():N}/reject", new { reason = "motivo" });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("Payable.NotFound", (await ReadJson(res)).GetProperty("title").GetString());
    }

    // ---------------------------------------------------------------------------
    // Cancel
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Cancel_PendingPayable_Returns_200_Cancelled_With_Reason()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);

        var createRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Para cancelar",
            installments = new[] { new { dueDate = "2026-07-10", amount = 2000m } },
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var payableId = (await ReadJson(createRes)).GetProperty("id").GetString()!;

        var cancelRes = await client.PostAsJsonAsync($"/api/payables/{payableId}/cancel", new { reason = "Pedido cancelado pelo cliente" });
        Assert.Equal(HttpStatusCode.OK, cancelRes.StatusCode);
        var body = await ReadJson(cancelRes);
        Assert.Equal("Cancelled", body.GetProperty("status").GetString());
        Assert.Equal("Pedido cancelado pelo cliente", body.GetProperty("cancelReason").GetString());
    }

    [Fact]
    public async Task Cancel_AlreadyCancelled_Returns_409_InvalidState()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);

        var createRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Duplo cancelamento",
            installments = new[] { new { dueDate = "2026-07-10", amount = 1000m } },
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var payableId = (await ReadJson(createRes)).GetProperty("id").GetString()!;

        var first = await client.PostAsJsonAsync($"/api/payables/{payableId}/cancel", new { reason = "primeiro" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsJsonAsync($"/api/payables/{payableId}/cancel", new { reason = "segundo" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("Payable.InvalidState", (await ReadJson(second)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Cancel_NonExistent_Returns_404_NotFound()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        var res = await client.PostAsJsonAsync($"/api/payables/payable_{Guid.NewGuid():N}/cancel", new { reason = "motivo" });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("Payable.NotFound", (await ReadJson(res)).GetProperty("title").GetString());
    }

    // ---------------------------------------------------------------------------
    // Update
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Update_Payable_Replaces_Installments_And_Proves_Orphan_Removal()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);

        // Create with 3 installments
        var createRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Antes da atualização",
            generate = new { totalAmount = 9000m, count = 3, firstDueDate = "2026-07-10", intervalMonths = 1 },
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var payableId = (await ReadJson(createRes)).GetProperty("id").GetString()!;

        // Update with 2 installments and new description
        var updateRes = await client.PutAsJsonAsync($"/api/payables/{payableId}", new
        {
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Depois da atualização",
            installments = new[]
            {
                new { dueDate = "2026-08-10", amount = 4000m },
                new { dueDate = "2026-09-10", amount = 6000m },
            },
        });
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);
        var updateBody = await ReadJson(updateRes);
        Assert.Equal(2, updateBody.GetProperty("installments").GetArrayLength());
        Assert.Equal(10000m, updateBody.GetProperty("totalAmount").GetDecimal());

        // GET and confirm EXACTLY 2 installments (proves orphan removal)
        var getRes = await client.GetAsync($"/api/payables/{payableId}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var getBody = await ReadJson(getRes);
        Assert.Equal("Depois da atualização", getBody.GetProperty("description").GetString());
        Assert.Equal(2, getBody.GetProperty("installments").GetArrayLength());
        Assert.Equal(10000m, getBody.GetProperty("totalAmount").GetDecimal());

        var amounts = getBody.GetProperty("installments").EnumerateArray()
            .Select(i => i.GetProperty("amount").GetDecimal())
            .OrderBy(a => a)
            .ToArray();
        Assert.Equal(new[] { 4000m, 6000m }, amounts);
    }

    [Fact]
    public async Task Update_RejectedPayable_Returns_409_NotEditable()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);

        var createRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Para rejeitar e tentar editar",
            installments = new[] { new { dueDate = "2026-07-10", amount = 500m } },
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var payableId = (await ReadJson(createRes)).GetProperty("id").GetString()!;

        var rejectRes = await client.PostAsJsonAsync($"/api/payables/{payableId}/reject", new { reason = "Rejeitado" });
        Assert.Equal(HttpStatusCode.OK, rejectRes.StatusCode);

        var updateRes = await client.PutAsJsonAsync($"/api/payables/{payableId}", new
        {
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Tentativa inválida",
            installments = new[] { new { dueDate = "2026-08-10", amount = 500m } },
        });
        Assert.Equal(HttpStatusCode.Conflict, updateRes.StatusCode);
        Assert.Equal("Payable.NotEditable", (await ReadJson(updateRes)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Update_NonExistent_Returns_404_NotFound()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);

        var res = await client.PutAsJsonAsync($"/api/payables/payable_{Guid.NewGuid():N}", new
        {
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Inexistente",
            installments = new[] { new { dueDate = "2026-08-10", amount = 100m } },
        });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("Payable.NotFound", (await ReadJson(res)).GetProperty("title").GetString());
    }

    // ---------------------------------------------------------------------------
    // Payment source override (per-payable bank account + method)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Create_With_PaymentBankAccountId_And_Method_GET_Reflects_Override()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);

        // Seed a bank account for the company.
        var bankRes = await client.PostAsJsonAsync("/api/bank-accounts", new
        {
            companyId = seed.CompanyId,
            bankCode = "341",
            bankName = "Itaú",
            agency = "1234",
            accountNumber = "99988877",
            accountDigit = "7",
            accountType = "Checking",
            alias = "Conta Pagamentos",
        });
        bankRes.EnsureSuccessStatusCode();
        var bankId = (await ReadJson(bankRes)).GetProperty("id").GetString()!;

        // Create payable with payment source override.
        var createRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Pagamento via Pix especificado",
            installments = new[] { new { dueDate = "2026-07-15", amount = 2500m } },
            paymentBankAccountId = bankId,
            paymentMethod = "Pix",
        });

        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var body = await ReadJson(createRes);
        Assert.Equal(bankId, body.GetProperty("paymentBankAccountId").GetString());
        Assert.Equal("Pix", body.GetProperty("paymentMethod").GetString());
    }
}
