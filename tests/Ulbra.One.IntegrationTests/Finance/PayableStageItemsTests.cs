using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ulbra.One.IntegrationTests.Finance;

[Collection("Postgres")]
public class PayableStageItemsTests(PostgresFixture fixture)
{
    [Fact]
    public async Task StageItems_returns_one_row_per_payable_with_total_and_installment_count()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var cc = await FinanceSeed.CreateCostCenterAsync(client, companyId, "CC1", "Geral");
        var vendor = await FinanceSeed.CreateVendorAsync(client, companyId, "Fornecedor A");

        // A single payable split into 3 installments of 10.000 (total 30.000).
        var createRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId,
            vendorId = vendor,
            costCenterId = cc,
            description = "Contratação Software House",
            installments = new[]
            {
                new { dueDate = today.AddDays(5).ToString("yyyy-MM-dd"), amount = 10_000m },
                new { dueDate = today.AddDays(35).ToString("yyyy-MM-dd"), amount = 10_000m },
                new { dueDate = today.AddDays(65).ToString("yyyy-MM-dd"), amount = 10_000m },
            },
        });
        createRes.EnsureSuccessStatusCode();

        // Payable-grain endpoint: exactly ONE row for the title.
        var stage = await client.GetAsync($"/api/payables/stage-items?companyId={companyId}");
        Assert.Equal(HttpStatusCode.OK, stage.StatusCode);
        var stageBody = JsonDocument.Parse(await stage.Content.ReadAsStringAsync()).RootElement;
        var items = stageBody.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        var row = items[0];
        Assert.Equal(3, row.GetProperty("installmentCount").GetInt32());
        Assert.Equal(30_000m, row.GetProperty("totalAmount").GetDecimal());
        Assert.Equal("Contratação Software House", row.GetProperty("description").GetString());
        Assert.Equal("Fornecedor A", row.GetProperty("counterpartyName").GetString());

        // The operational (per-installment) list still returns one row PER installment — proving the grain differs.
        var list = await client.GetAsync($"/api/payables?companyId={companyId}");
        var listBody = JsonDocument.Parse(await list.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(3, listBody.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task StageItems_filters_by_hasFiscalDocument()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var today = FinanceSeed.Today();

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var cc = await FinanceSeed.CreateCostCenterAsync(client, companyId, "CC1", "Geral");
        var vendor = await FinanceSeed.CreateVendorAsync(client, companyId, "Fornecedor A");

        // Two approved payables parked in PendingAccounting: one still missing its NF, one that already has it.
        var withoutNf = await FinanceSeed.CreatePayableAsync(factory, client, companyId, vendor, cc, today.AddDays(10), 100m, approve: false);
        await FinanceSeed.ApproveAsync(client, withoutNf);

        var withNf = await FinanceSeed.CreatePayableAsync(factory, client, companyId, vendor, cc, today.AddDays(10), 200m, approve: false);
        await FinanceSeed.ApproveAsync(client, withNf);
        await FinanceSeed.RegisterFiscalDocAsync(factory, client, withNf);

        // A parsed FiscalDocument and a plain attached document BOTH count as "has NF".
        var withAttachment = await FinanceSeed.CreatePayableAsync(factory, client, companyId, vendor, cc, today.AddDays(10), 300m, approve: false);
        await FinanceSeed.ApproveAsync(client, withAttachment);
        await FinanceSeed.AttachDocumentAsync(client, withAttachment);

        // hasFiscalDocument=false => only the payable with no NF at all (the Pendências "Nota Fiscal" tab).
        Assert.Equal([withoutNf], await StageItemIds(client, companyId, "PendingAccounting", hasFiscalDocument: false));

        // hasFiscalDocument=true => the fiscal-doc one AND the attachment one (order by due date is not asserted).
        var withNfSet = await StageItemIds(client, companyId, "PendingAccounting", hasFiscalDocument: true);
        Assert.Equal(2, withNfSet.Count);
        Assert.Contains(withNf, withNfSet);
        Assert.Contains(withAttachment, withNfSet);

        // Absent filter => all three PendingAccounting payables (previous behavior preserved for the payables tabs).
        var all = await StageItemIds(client, companyId, "PendingAccounting", hasFiscalDocument: null);
        Assert.Equal(3, all.Count);
    }

    private static async Task<List<string>> StageItemIds(HttpClient client, string companyId, string status, bool? hasFiscalDocument)
    {
        var url = $"/api/payables/stage-items?companyId={companyId}&status={status}";
        if (hasFiscalDocument is { } h) url += $"&hasFiscalDocument={(h ? "true" : "false")}";
        var res = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        return body.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("payableId").GetString()!)
            .ToList();
    }
}
