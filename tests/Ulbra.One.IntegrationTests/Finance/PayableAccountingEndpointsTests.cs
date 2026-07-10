using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ulbra.One.IntegrationTests.Finance;

/// <summary>Endpoints of the accounting stage: the composite accounting-view read model, the Nota Fiscal
/// gate on accounting/confirm, and the PendingAccounting stage-summary bucket.</summary>
[Collection("Postgres")]
public class PayableAccountingEndpointsTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private static async Task<(string CompanyId, string PayableId)> ApprovedPayableAsync(
        ApiFactory factory, HttpClient client, decimal amount = 1000m)
    {
        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var cc = await FinanceSeed.CreateCostCenterAsync(client, companyId, "CC1", "TI");
        var vendor = await FinanceSeed.CreateVendorAsync(client, companyId, "Fornecedor Contab");
        var payableId = await FinanceSeed.CreatePayableAsync(
            factory, client, companyId, vendor, cc, FinanceSeed.Today().AddDays(30), amount, approve: false);
        // Approve → PendingAccounting (approval no longer schedules).
        (await client.PostAsJsonAsync($"/api/payables/{payableId}/approve", new { })).EnsureSuccessStatusCode();
        return (companyId, payableId);
    }

    [Fact]
    public async Task Confirm_Without_FiscalDocument_Returns_400_And_View_Shows_Pending()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var (_, payableId) = await ApprovedPayableAsync(factory, client);

        var afterApprove = await ReadJson(await client.GetAsync($"/api/payables/{payableId}"));
        Assert.Equal("PendingAccounting", afterApprove.GetProperty("status").GetString());

        // Composite accounting-view: vendor tax block + money, no NF yet.
        var view = await ReadJson(await client.GetAsync($"/api/payables/{payableId}/accounting-view"));
        Assert.False(view.GetProperty("hasFiscalDocument").GetBoolean());
        Assert.Equal(1000m, view.GetProperty("totalAmount").GetDecimal());
        Assert.Equal("Fornecedor Contab", view.GetProperty("vendor").GetProperty("name").GetString());

        // Cannot contabilizar without a registered Nota Fiscal.
        var confirm = await client.PostAsJsonAsync($"/api/payables/{payableId}/accounting/confirm", new { });
        Assert.Equal(HttpStatusCode.BadRequest, confirm.StatusCode);
        Assert.Equal("Payable.FiscalDocumentMissing", (await ReadJson(confirm)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Confirm_With_FiscalDocument_Schedules_Payable()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var (_, payableId) = await ApprovedPayableAsync(factory, client);

        await FinanceSeed.RegisterFiscalDocAsync(factory, client, payableId);

        var viewAfterNf = await ReadJson(await client.GetAsync($"/api/payables/{payableId}/accounting-view"));
        Assert.True(viewAfterNf.GetProperty("hasFiscalDocument").GetBoolean());

        var confirm = await client.PostAsJsonAsync($"/api/payables/{payableId}/accounting/confirm", new { });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        Assert.Equal("Scheduled", (await ReadJson(confirm)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Confirm_With_AttachedDocument_Schedules_Payable()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var (_, payableId) = await ApprovedPayableAsync(factory, client);

        // No parsed FiscalDocument — just an attached NF document (PayableAttachment, the Pendências flow).
        // It satisfies the "has NF" gate all the same.
        await FinanceSeed.AttachDocumentAsync(client, payableId);

        var view = await ReadJson(await client.GetAsync($"/api/payables/{payableId}/accounting-view"));
        Assert.True(view.GetProperty("hasFiscalDocument").GetBoolean());

        var confirm = await client.PostAsJsonAsync($"/api/payables/{payableId}/accounting/confirm", new { });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        Assert.Equal("Scheduled", (await ReadJson(confirm)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task StageSummary_Counts_PendingAccounting_In_Accounting_Bucket()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var (companyId, _) = await ApprovedPayableAsync(factory, client, amount: 250m);

        var summary = await ReadJson(await client.GetAsync($"/api/payables/stage-summary?companyId={companyId}"));
        Assert.Equal(1, summary.GetProperty("accounting").GetProperty("count").GetInt32());
        Assert.Equal(250m, summary.GetProperty("accounting").GetProperty("totalAmount").GetDecimal());
    }
}
