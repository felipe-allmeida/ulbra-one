using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ulbra.One.IntegrationTests;

namespace Ulbra.One.IntegrationTests.Finance;

[Collection("Postgres")]
public class PaymentsReportEndpointsTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private static async Task EnsureOk(HttpResponseMessage res)
    {
        if (!res.IsSuccessStatusCode)
            throw new Xunit.Sdk.XunitException($"{(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
    }

    // A Manual payable has no originating requisition → it is "imported/external", hidden by default;
    // the report tests pass showImported=true to include it.
    private async Task<(string CompanyId, DateOnly Due)> SeedScheduledPayableAsync(HttpClient client, decimal amount = 4732.82m)
    {
        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var vendor = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Relatório LTDA");
        var costCenter = await FinanceSeed.CreateCostCenterAsync(client, companyId, "6060", "ENSINO");
        var due = FinanceSeed.Today().AddDays(5);
        await FinanceSeed.CreatePayableAsync(fixture.Api, client, companyId, vendor, costCenter, due, amount);
        return (companyId, due);
    }

    [Fact]
    public async Task Report_lists_scheduled_payable_in_due_window()
    {
        var client = await fixture.Api.CreateAdminClientAsync();
        var (companyId, due) = await SeedScheduledPayableAsync(client);

        var res = await client.GetAsync(
            $"/api/payables/report?companyId={companyId}&dueFrom={due:yyyy-MM-dd}&dueTo={due:yyyy-MM-dd}&showImported=true");
        res.EnsureSuccessStatusCode();

        var body = await ReadJson(res);
        Assert.True(body.GetProperty("total").GetInt32() >= 1);
        var first = body.GetProperty("items")[0];
        Assert.Equal("Fila Pagamento", first.GetProperty("statusLabel").GetString());
        Assert.Equal("ENSINO", first.GetProperty("natureName").GetString());
        Assert.Equal(4732.82m, first.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Report_respects_the_imported_toggle()
    {
        var client = await fixture.Api.CreateAdminClientAsync();
        var (companyId, due) = await SeedScheduledPayableAsync(client);
        var window = $"companyId={companyId}&dueFrom={due:yyyy-MM-dd}&dueTo={due:yyyy-MM-dd}";

        // Default view hides payables without a requisition (the Manual one seeded here).
        var hidden = await ReadJson(await client.GetAsync($"/api/payables/report?{window}"));
        Assert.Equal(0, hidden.GetProperty("total").GetInt32());

        // With the toggle it shows.
        var shown = await ReadJson(await client.GetAsync($"/api/payables/report?{window}&showImported=true"));
        Assert.True(shown.GetProperty("total").GetInt32() >= 1);
    }

    [Fact]
    public async Task Report_export_returns_pdf()
    {
        var client = await fixture.Api.CreateAdminClientAsync();
        var (companyId, due) = await SeedScheduledPayableAsync(client);

        var res = await client.GetAsync(
            $"/api/payables/report/export?companyId={companyId}&dueFrom={due:yyyy-MM-dd}&dueTo={due:yyyy-MM-dd}&showImported=true");
        await EnsureOk(res);
        Assert.Equal("application/pdf", res.Content.Headers.ContentType?.MediaType);

        var bytes = await res.Content.ReadAsByteArrayAsync();
        // "%PDF" magic — proves MigraDoc rendered with the embedded font on the server.
        Assert.True(bytes.Length > 0 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46);
    }

    [Fact]
    public async Task Report_summary_aggregates_total_and_breakdowns()
    {
        var client = await fixture.Api.CreateAdminClientAsync();
        var (companyId, due) = await SeedScheduledPayableAsync(client);

        var res = await client.GetAsync(
            $"/api/payables/report/summary?companyId={companyId}&dueFrom={due:yyyy-MM-dd}&dueTo={due:yyyy-MM-dd}&showImported=true");
        await EnsureOk(res);

        var body = await ReadJson(res);
        Assert.True(body.GetProperty("count").GetInt32() >= 1);
        Assert.True(body.GetProperty("total").GetDecimal() >= 4732.82m);
        Assert.True(body.GetProperty("byProvider").GetArrayLength() >= 1);
        Assert.True(body.GetProperty("byUnit").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Report_forbidden_without_payables_read()
    {
        var client = fixture.Api.CreateClientWith("Finance"); // role without payables:read
        var res = await client.GetAsync($"/api/payables/report?companyId={Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task RemittanceLines_returns_a_paginated_envelope()
    {
        var client = await fixture.Api.CreateAdminClientAsync();
        var companyId = await FinanceSeed.CreateCompanyAsync(client);

        var res = await client.GetAsync($"/api/remittances/lines?companyId={companyId}");
        res.EnsureSuccessStatusCode();

        var body = await ReadJson(res);
        Assert.Equal(0, body.GetProperty("total").GetInt32());
        Assert.Equal(0, body.GetProperty("items").GetArrayLength());
    }
}
