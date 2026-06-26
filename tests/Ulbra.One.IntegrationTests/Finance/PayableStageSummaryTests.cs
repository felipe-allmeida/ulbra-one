using System.Net;
using System.Text.Json;

namespace Ulbra.One.IntegrationTests.Finance;

[Collection("Postgres")]
public class PayableStageSummaryTests(PostgresFixture fixture)
{
    [Fact]
    public async Task StageSummary_groups_count_and_total_by_stage()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var today = FinanceSeed.Today();

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var cc = await FinanceSeed.CreateCostCenterAsync(client, companyId, "CC1", "Geral");
        var vendor = await FinanceSeed.CreateVendorAsync(client, companyId, "Fornecedor A");

        // Two payables left in the default 'Received' stage (Entry); one approved -> Scheduled (Payment).
        await FinanceSeed.CreatePayableAsync(client, companyId, vendor, cc, today.AddDays(10), 100m, approve: false);
        await FinanceSeed.CreatePayableAsync(client, companyId, vendor, cc, today.AddDays(10), 200m, approve: false);
        await FinanceSeed.CreatePayableAsync(client, companyId, vendor, cc, today.AddDays(10), 50m, approve: true);

        var res = await client.GetAsync($"/api/payables/stage-summary?companyId={companyId}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        var entry = body.GetProperty("entry");
        Assert.Equal(2, entry.GetProperty("count").GetInt32());
        Assert.Equal(300m, entry.GetProperty("totalAmount").GetDecimal());

        var payment = body.GetProperty("payment");
        Assert.Equal(1, payment.GetProperty("count").GetInt32());
        Assert.Equal(50m, payment.GetProperty("totalAmount").GetDecimal());

        // Stages with no payables report zero, not null.
        Assert.Equal(0, body.GetProperty("classification").GetProperty("count").GetInt32());
        Assert.Equal(0m, body.GetProperty("reconciled").GetProperty("totalAmount").GetDecimal());
    }
}
