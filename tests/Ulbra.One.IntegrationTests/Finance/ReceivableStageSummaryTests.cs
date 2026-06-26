using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ulbra.One.IntegrationTests.Finance;

[Collection("Postgres")]
public class ReceivableStageSummaryTests(PostgresFixture fixture)
{
    [Fact]
    public async Task StageSummary_splits_entry_and_receipt_by_received_amount()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var today = FinanceSeed.Today();

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var cc = await FinanceSeed.CreateCostCenterAsync(client, companyId, "CC1", "Geral");
        var customer = await FinanceSeed.CreateCustomerAsync(client, companyId, "Cliente A");
        var bank = await FinanceSeed.CreateBankAccountAsync(client, companyId, "00012345");

        // Two untouched (Entry); one with a partial receipt (Receipt).
        await FinanceSeed.CreateReceivableAsync(client, companyId, customer, cc, today.AddDays(10), 100m);
        await FinanceSeed.CreateReceivableAsync(client, companyId, customer, cc, today.AddDays(10), 200m);
        var (rcv, inst) = await FinanceSeed.CreateReceivableAsync(client, companyId, customer, cc, today.AddDays(10), 300m);
        await FinanceSeed.RegisterReceiptAsync(client, rcv, inst, bank, 120m, today);

        var res = await client.GetAsync($"/api/receivables/stage-summary?companyId={companyId}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(2, body.GetProperty("entry").GetProperty("count").GetInt32());
        Assert.Equal(300m, body.GetProperty("entry").GetProperty("totalAmount").GetDecimal());
        Assert.Equal(1, body.GetProperty("receipt").GetProperty("count").GetInt32());
        Assert.Equal(300m, body.GetProperty("receipt").GetProperty("totalAmount").GetDecimal());
        Assert.Equal(0, body.GetProperty("reconciled").GetProperty("count").GetInt32());
    }
}
