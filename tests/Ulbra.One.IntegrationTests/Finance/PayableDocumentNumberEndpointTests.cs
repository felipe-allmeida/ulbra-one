using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ulbra.One.IntegrationTests.Finance;

/// <summary>POST /api/payables/{id}/document-number — Contábil sets/corrects the número do documento from the
/// Revisão de Contabilidade drawer. Covers the 200 happy path (trim + persist), 404, and editing a **paid**
/// payable (número is fiscal metadata — must stay editable after payment).</summary>
[Collection("Postgres")]
public class PayableDocumentNumberEndpointTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private static async Task<string> CreateEditablePayableAsync(ApiFactory factory, HttpClient client)
    {
        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, "CC1", "TI");
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Doc");
        // approve:false keeps it in Received (mutable) — the accounting review states are equally editable.
        return await FinanceSeed.CreatePayableAsync(factory, client, companyId, vendorId, costCenterId,
            FinanceSeed.Today().AddDays(30), 1000m, approve: false);
    }

    [Fact]
    public async Task Set_document_number_persists_and_trims()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var payableId = await CreateEditablePayableAsync(factory, client);

        var res = await client.PostAsJsonAsync($"/api/payables/{payableId}/document-number",
            new { documentNumber = "  NF-INT-42  " });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("NF-INT-42", (await ReadJson(res)).GetProperty("documentNumber").GetString());

        var reloaded = await ReadJson(await client.GetAsync($"/api/payables/{payableId}"));
        Assert.Equal("NF-INT-42", reloaded.GetProperty("documentNumber").GetString());
    }

    [Fact]
    public async Task Set_document_number_returns_404_for_unknown_payable()
    {
        var client = await fixture.Api.CreateAdminClientAsync();
        var res = await client.PostAsJsonAsync(
            $"/api/payables/payable_{Guid.NewGuid():N}/document-number", new { documentNumber = "NF-1" });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Set_document_number_allowed_on_paid_payable()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, "CC1", "TI");
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Doc");
        // Drive to Paid: approve + complete accounting (→ Scheduled), then settle the single installment in full.
        var (payableId, installmentId) = await FinanceSeed.CreatePayableWithInstallmentAsync(
            factory, client, companyId, vendorId, costCenterId, FinanceSeed.Today(), 1000m, approve: true);
        var bankId = await FinanceSeed.CreateBankAccountAsync(client, companyId, "12345");
        await FinanceSeed.RegisterPaymentAsync(client, payableId, installmentId, bankId, 1000m, FinanceSeed.Today());

        var paid = await ReadJson(await client.GetAsync($"/api/payables/{payableId}"));
        Assert.Equal("Paid", paid.GetProperty("status").GetString());

        var res = await client.PostAsJsonAsync($"/api/payables/{payableId}/document-number",
            new { documentNumber = "NF-PAGA-1" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("NF-PAGA-1", (await ReadJson(res)).GetProperty("documentNumber").GetString());

        var reloaded = await ReadJson(await client.GetAsync($"/api/payables/{payableId}"));
        Assert.Equal("NF-PAGA-1", reloaded.GetProperty("documentNumber").GetString());
    }
}
