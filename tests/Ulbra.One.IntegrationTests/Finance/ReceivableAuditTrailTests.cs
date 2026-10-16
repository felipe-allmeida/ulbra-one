using System.Net;
using System.Text.Json;

namespace Ulbra.One.IntegrationTests.Finance;

[Collection("Postgres")]
public class ReceivableAuditTrailTests(PostgresFixture fixture)
{
    [Fact]
    public async Task AuditTrail_returns_created_then_receipt_registered()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var today = FinanceSeed.Today();

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var cc = await FinanceSeed.CreateCostCenterAsync(client, companyId, "CC1", "Geral");
        var customer = await FinanceSeed.CreateCustomerAsync(client, companyId, "Cliente A");
        var bank = await FinanceSeed.CreateBankAccountAsync(client, companyId, "00012345");
        var (rcv, inst) = await FinanceSeed.CreateReceivableAsync(client, companyId, customer, cc, today.AddDays(5), 100m);
        await FinanceSeed.RegisterReceiptAsync(client, rcv, inst, bank, 100m, today);

        var res = await client.GetAsync($"/api/receivables/{rcv}/audit-trail");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        var types = body.EnumerateArray().Select(e => e.GetProperty("type").GetString()).ToList();
        Assert.Equal("Created", types[0]);
        Assert.Contains("ReceiptRegistered", types);
    }

    [Fact]
    public async Task AuditTrail_returns_404_for_unknown_receivable()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var res = await client.GetAsync($"/api/receivables/{Guid.NewGuid()}/audit-trail");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
