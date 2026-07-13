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
        ApiFactory factory, HttpClient client, decimal amount = 1000m, string origin = "Manual")
    {
        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var cc = await FinanceSeed.CreateCostCenterAsync(client, companyId, "CC1", "TI");
        var vendor = await FinanceSeed.CreateVendorAsync(client, companyId, "Fornecedor Contab");
        var payableId = await FinanceSeed.CreatePayableAsync(
            factory, client, companyId, vendor, cc, FinanceSeed.Today().AddDays(30), amount, approve: false, origin);
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
    public async Task Confirm_Advance_Without_FiscalDocument_Schedules_Payable()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        // Adiantamento: paid before the NF exists — exempt from the fiscal-document gate.
        var (_, payableId) = await ApprovedPayableAsync(factory, client, origin: "Advance");

        var view = await ReadJson(await client.GetAsync($"/api/payables/{payableId}/accounting-view"));
        Assert.Equal("Advance", view.GetProperty("origin").GetString());
        Assert.False(view.GetProperty("hasFiscalDocument").GetBoolean());

        var confirm = await client.PostAsJsonAsync($"/api/payables/{payableId}/accounting/confirm", new { });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        Assert.Equal("Scheduled", (await ReadJson(confirm)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task FiscalDocumentPending_Bucket_And_Filter_Exclude_Advance()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var cc = await FinanceSeed.CreateCostCenterAsync(client, companyId, "CC1", "TI");
        var vendor = await FinanceSeed.CreateVendorAsync(client, companyId, "Fornecedor Contab");

        // Two PendingAccounting payables without NF: a normal (Manual) one and an adiantamento (Advance).
        var manualId = await FinanceSeed.CreatePayableAsync(
            factory, client, companyId, vendor, cc, FinanceSeed.Today().AddDays(30), 100m, approve: false);
        await FinanceSeed.ApproveAsync(client, manualId);
        var advanceId = await FinanceSeed.CreatePayableAsync(
            factory, client, companyId, vendor, cc, FinanceSeed.Today().AddDays(30), 200m, approve: false, origin: "Advance");
        await FinanceSeed.ApproveAsync(client, advanceId);

        // Only the Manual payable is an NF pendency — the Advance one is exempt from the gate.
        var summary = await ReadJson(await client.GetAsync($"/api/payables/stage-summary?companyId={companyId}"));
        Assert.Equal(1, summary.GetProperty("fiscalDocumentPending").GetProperty("count").GetInt32());
        Assert.Equal(100m, summary.GetProperty("fiscalDocumentPending").GetProperty("totalAmount").GetDecimal());
        Assert.Equal(2, summary.GetProperty("accounting").GetProperty("count").GetInt32());

        var items = await ReadJson(await client.GetAsync(
            $"/api/payables/stage-items?companyId={companyId}&status=PendingAccounting&hasFiscalDocument=false"));
        var ids = items.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("payableId").GetString()).ToList();
        Assert.Contains(manualId, ids);
        Assert.DoesNotContain(advanceId, ids);
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
