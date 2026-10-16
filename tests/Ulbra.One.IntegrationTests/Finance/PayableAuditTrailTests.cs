using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ulbra.One.IntegrationTests.Finance;

[Collection("Postgres")]
public class PayableAuditTrailTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task AuditTrail_returns_created_classified_approved_for_a_scheduled_payable()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var today = FinanceSeed.Today();

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var cc = await FinanceSeed.CreateCostCenterAsync(client, companyId, "CC1", "Geral");
        var vendor = await FinanceSeed.CreateVendorAsync(client, "Fornecedor A");

        // Create payable without approving yet — we need to classify first.
        var payableId = await FinanceSeed.CreatePayableAsync(factory, client, companyId, vendor, cc, today.AddDays(10), 100m, approve: false);

        // Classify (requires valid ChartAccounts of the right type).
        var accId = await FinanceSeed.CreateAccountingAccountAsync(client);
        var classifyRes = await client.PostAsJsonAsync($"/api/payables/{payableId}/classify",
            new { accountingAccountId = accId });
        classifyRes.EnsureSuccessStatusCode();

        // Approve after classification.
        var approveRes = await client.PostAsJsonAsync($"/api/payables/{payableId}/approve", new { });
        approveRes.EnsureSuccessStatusCode();

        var res = await client.GetAsync($"/api/payables/{payableId}/audit-trail");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        var types = body.EnumerateArray().Select(e => e.GetProperty("type").GetString()).ToList();
        Assert.Contains("Created", types);
        Assert.Contains("Classified", types);
        Assert.Contains("Approved", types);

        // Actor name resolved to the admin display name (not a raw guid).
        var createdEvent = body.EnumerateArray().First(e => e.GetProperty("type").GetString() == "Created");
        Assert.Equal("Admin Teste", createdEvent.GetProperty("actorName").GetString());

        // Timestamps are ascending.
        var timestamps = body.EnumerateArray()
            .Select(e => e.GetProperty("timestamp").GetDateTimeOffset())
            .ToList();
        for (var i = 1; i < timestamps.Count; i++)
            Assert.True(timestamps[i - 1] <= timestamps[i]);
    }

    [Fact]
    public async Task AuditTrail_records_a_durable_Rejected_event_with_reason()
    {
        // AEL-935: the reject/return event is sourced from the durable audit history (not current fields), so it
        // survives a later Reopen. This proves the whole path: interceptor writes the reject → reader reads it →
        // builder synthesizes the event with the reason.
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var today = FinanceSeed.Today();
        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var cc = await FinanceSeed.CreateCostCenterAsync(client, companyId, "CC1", "Geral");
        var vendor = await FinanceSeed.CreateVendorAsync(client, "Fornecedor A");
        var payableId = await FinanceSeed.CreatePayableAsync(factory, client, companyId, vendor, cc, today.AddDays(10), 100m, approve: false);

        var rejectRes = await client.PostAsJsonAsync($"/api/payables/{payableId}/reject", new { reason = "documento divergente" });
        rejectRes.EnsureSuccessStatusCode();

        var body = await ReadJson(await client.GetAsync($"/api/payables/{payableId}/audit-trail"));
        var rejected = body.EnumerateArray().FirstOrDefault(e => e.GetProperty("type").GetString() == "Rejected");
        Assert.Equal(JsonValueKind.Object, rejected.ValueKind); // the event is present in the trail
        Assert.Equal("documento divergente", rejected.GetProperty("metadata").GetProperty("reason").GetString());
    }

    [Fact]
    public async Task AuditTrail_returns_404_for_unknown_payable()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var res = await client.GetAsync($"/api/payables/{Guid.NewGuid()}/audit-trail");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
