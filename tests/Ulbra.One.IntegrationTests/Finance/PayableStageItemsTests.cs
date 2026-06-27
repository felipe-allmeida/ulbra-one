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
}
