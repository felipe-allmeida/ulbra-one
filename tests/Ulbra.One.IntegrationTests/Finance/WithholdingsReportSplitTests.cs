using System.Net.Http.Json;
using System.Text.Json;

namespace Ulbra.One.IntegrationTests.Finance;

/// <summary>GET /api/payables/withholdings must split each retenção into retido (realized) vs a reter (foreseen):
/// emission taxes (IRRF/INSS/ISS) are retido from the competência; the CSRF group (PIS/COFINS/CSLL) stays
/// "a reter" until paid, then becomes retido proportionally to the baixa.</summary>
[Collection("Postgres")]
public class WithholdingsReportSplitTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private static (decimal Realized, decimal Foreseen) Tax(JsonElement byTax, string taxType)
    {
        for (var i = 0; i < byTax.GetArrayLength(); i++)
        {
            var r = byTax[i];
            if (r.GetProperty("taxType").GetString() == taxType)
                return (r.GetProperty("realizedAmount").GetDecimal(), r.GetProperty("foreseenAmount").GetDecimal());
        }
        return (0m, 0m);
    }

    [Fact]
    public async Task Report_shows_csrf_as_foreseen_until_paid_then_realized()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var ccId = await FinanceSeed.CreateCostCenterAsync(client, companyId, "CC-R", "Retencoes");
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Ret");
        var bankId = await FinanceSeed.CreateBankAccountAsync(client, companyId, "55555-5");

        // Product payable so withholdings are driven manually (no auto-calc); one installment of 1000.
        var (payableId, installmentId) = await FinanceSeed.CreatePayableWithInstallmentAsync(
            factory, client, companyId, vendorId, ccId, new DateOnly(2026, 9, 1), 1000m, approve: false);

        // Emission: IRRF 150. CSRF: PIS 6.50 + COFINS 30 + CSLL 10 = 46.50. Net = 1000 - 196.50 = 803.50.
        var put = await client.PutAsJsonAsync($"/api/payables/{payableId}/withholdings", new
        {
            items = new object[]
            {
                new { taxType = "Irrf", baseAmount = 1000m, rate = (decimal?)0.015m, amount = 150m },
                new { taxType = "Pis", baseAmount = 1000m, rate = (decimal?)0.0065m, amount = 6.5m },
                new { taxType = "Cofins", baseAmount = 1000m, rate = (decimal?)0.03m, amount = 30m },
                new { taxType = "Csll", baseAmount = 1000m, rate = (decimal?)0.01m, amount = 10m },
            },
        });
        put.EnsureSuccessStatusCode();

        // ── BEFORE payment: only IRRF is retido; CSRF is entirely "a reter" ──
        var before = await ReadJson(await client.GetAsync($"/api/payables/withholdings?companyId={companyId}"));
        var t0 = before.GetProperty("totals");
        Assert.Equal(150m, t0.GetProperty("realizedTotal").GetDecimal());
        Assert.Equal(46.5m, t0.GetProperty("foreseenTotal").GetDecimal());

        var byTax0 = before.GetProperty("byTax");
        Assert.Equal((150m, 0m), Tax(byTax0, "Irrf"));
        Assert.Equal((0m, 6.5m), Tax(byTax0, "Pis"));
        Assert.Equal((0m, 30m), Tax(byTax0, "Cofins"));
        Assert.Equal((0m, 10m), Tax(byTax0, "Csll"));

        // Schedule via an attachment (satisfies the NF gate WITHOUT re-running auto-withholding, which would
        // clear a Product's manual rows), then pay the full net.
        await FinanceSeed.ApproveAsync(client, payableId);
        await FinanceSeed.AttachDocumentAsync(client, payableId);
        await FinanceSeed.ConfirmAccountingAsync(client, payableId); // → Scheduled
        await FinanceSeed.RegisterPaymentAsync(client, payableId, installmentId, bankId, 803.5m, new DateOnly(2026, 9, 10));

        // ── AFTER full payment: CSRF is now fully retido; nothing foreseen ──
        var after = await ReadJson(await client.GetAsync($"/api/payables/withholdings?companyId={companyId}"));
        var t1 = after.GetProperty("totals");
        Assert.Equal(196.5m, t1.GetProperty("realizedTotal").GetDecimal());
        Assert.Equal(0m, t1.GetProperty("foreseenTotal").GetDecimal());

        var byTax1 = after.GetProperty("byTax");
        Assert.Equal((150m, 0m), Tax(byTax1, "Irrf"));
        Assert.Equal((6.5m, 0m), Tax(byTax1, "Pis"));
        Assert.Equal((30m, 0m), Tax(byTax1, "Cofins"));
        Assert.Equal((10m, 0m), Tax(byTax1, "Csll"));
    }
}
