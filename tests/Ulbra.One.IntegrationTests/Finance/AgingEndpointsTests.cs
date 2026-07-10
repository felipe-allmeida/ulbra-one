using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ulbra.One.IntegrationTests.Finance;

[Collection("Postgres")]
public class AgingEndpointsTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private sealed record Seed(string CompanyId, string CostCenterId, string VendorId);

    /// <summary>
    /// Seeds one company with two vendors / two cost centers and a mix of installments across buckets:
    /// vendor A / CC1: NotYetDue 100, Overdue1To30 200; vendor B / CC2: Overdue31To60 300, partial (remaining 50 in 1-30);
    /// plus an excluded fully-paid installment and an excluded cancelled payable.
    /// </summary>
    private async Task<(string CompanyId, string VendorA, string VendorB, string Cc1, string Cc2)> SeedAgingAsync(ApiFactory factory, HttpClient client, DateOnly today)
    {
        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var cc1 = await FinanceSeed.CreateCostCenterAsync(client, companyId, "CC1", "Marketing");
        var cc2 = await FinanceSeed.CreateCostCenterAsync(client, companyId, "CC2", "TI");
        var vendorA = await FinanceSeed.CreateVendorAsync(client, companyId, "Fornecedor A", FinanceSeed.RandomCnpj());
        var vendorB = await FinanceSeed.CreateVendorAsync(client, companyId, "Fornecedor B", FinanceSeed.RandomCnpj());
        var bankId = await FinanceSeed.CreateBankAccountAsync(client, companyId, "00012345");

        // Vendor A / CC1
        await FinanceSeed.CreatePayableAsync(factory, client, companyId, vendorA, cc1, today.AddDays(10), 100m);   // NotYetDue
        await FinanceSeed.CreatePayableAsync(factory, client, companyId, vendorA, cc1, today.AddDays(-10), 200m);  // Overdue1To30

        // Vendor B / CC2
        await FinanceSeed.CreatePayableAsync(factory, client, companyId, vendorB, cc2, today.AddDays(-45), 300m);  // Overdue31To60

        // Vendor B / CC2 partial: amount 80, pay 30, remaining 50 -> Overdue1To30
        var (partialPayable, partialInst) = await FinanceSeed.CreatePayableWithInstallmentAsync(factory, client, companyId, vendorB, cc2, today.AddDays(-5), 80m);
        await FinanceSeed.RegisterPaymentAsync(client, partialPayable, partialInst, bankId, 30m, today);

        // Excluded: fully paid installment (vendor A / CC1)
        var (paidPayable, paidInst) = await FinanceSeed.CreatePayableWithInstallmentAsync(factory, client, companyId, vendorA, cc1, today.AddDays(-3), 500m);
        await FinanceSeed.RegisterPaymentAsync(client, paidPayable, paidInst, bankId, 500m, today);

        // Excluded: cancelled payable (vendor A / CC1)
        var cancelledPayable = await FinanceSeed.CreatePayableAsync(factory, client, companyId, vendorA, cc1, today.AddDays(-20), 999m, approve: false);
        var cancelRes = await client.PostAsJsonAsync($"/api/payables/{cancelledPayable}/cancel", new { reason = "Nao vai mais" });
        cancelRes.EnsureSuccessStatusCode();

        return (companyId, vendorA, vendorB, cc1, cc2);
    }

    [Fact]
    public async Task GetAgingReport_Returns_BucketTotals_Excluding_Paid_And_Cancelled_Using_Remaining()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (companyId, _, _, _, _) = await SeedAgingAsync(factory, client, today);

        var res = await client.GetAsync($"/api/payables/aging?companyId={companyId}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await ReadJson(res);
        var totals = body.GetProperty("totals");

        Assert.Equal(100m, totals.GetProperty("notYetDue").GetDecimal());
        Assert.Equal(250m, totals.GetProperty("overdue1To30").GetDecimal()); // 200 + remaining 50
        Assert.Equal(300m, totals.GetProperty("overdue31To60").GetDecimal());
        Assert.Equal(0m, totals.GetProperty("overdue61To90").GetDecimal());
        Assert.Equal(0m, totals.GetProperty("overdue90Plus").GetDecimal());
        Assert.Equal(650m, totals.GetProperty("grandTotal").GetDecimal());
    }

    [Fact]
    public async Task GetAgingReport_ByVendor_HasRow_Per_Vendor_With_Name_Ordered_By_Total_Desc()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (companyId, vendorA, vendorB, _, _) = await SeedAgingAsync(factory, client, today);

        var body = await ReadJson(await client.GetAsync($"/api/payables/aging?companyId={companyId}"));
        var byVendor = body.GetProperty("byVendor");
        Assert.Equal(2, byVendor.GetArrayLength());

        // Vendor B total = 300 + 50 = 350; Vendor A total = 100 + 200 = 300. Ordered desc -> B first.
        Assert.Equal(vendorB, byVendor[0].GetProperty("vendorId").GetString());
        Assert.Equal("Fornecedor B", byVendor[0].GetProperty("name").GetString());
        Assert.Equal(350m, byVendor[0].GetProperty("total").GetDecimal());
        Assert.Equal(300m, byVendor[0].GetProperty("overdue31To60").GetDecimal());
        Assert.Equal(50m, byVendor[0].GetProperty("overdue1To30").GetDecimal());

        Assert.Equal(vendorA, byVendor[1].GetProperty("vendorId").GetString());
        Assert.Equal("Fornecedor A", byVendor[1].GetProperty("name").GetString());
        Assert.Equal(300m, byVendor[1].GetProperty("total").GetDecimal());
        Assert.Equal(100m, byVendor[1].GetProperty("notYetDue").GetDecimal());
        Assert.Equal(200m, byVendor[1].GetProperty("overdue1To30").GetDecimal());
    }

    [Fact]
    public async Task GetAgingReport_ByCostCenter_HasRow_Per_CostCenter_IdOnly_Ordered_By_Total_Desc()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (companyId, _, _, cc1, cc2) = await SeedAgingAsync(factory, client, today);

        var body = await ReadJson(await client.GetAsync($"/api/payables/aging?companyId={companyId}"));
        var byCostCenter = body.GetProperty("byCostCenter");
        Assert.Equal(2, byCostCenter.GetArrayLength());

        // CC2 total = 300 + 50 = 350; CC1 total = 100 + 200 = 300. Ordered desc -> CC2 first.
        Assert.Equal(cc2, byCostCenter[0].GetProperty("costCenterId").GetString());
        Assert.Equal(350m, byCostCenter[0].GetProperty("total").GetDecimal());
        Assert.False(byCostCenter[0].TryGetProperty("name", out _));

        Assert.Equal(cc1, byCostCenter[1].GetProperty("costCenterId").GetString());
        Assert.Equal(300m, byCostCenter[1].GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task GetAgingReport_Includes_PendingApproval_Payables()
    {
        // Decision locked: aging report includes PendingApproval payables (status != Cancelled/Rejected,
        // installment not fully Paid). A management view must surface titles due/overdue even if
        // still awaiting approval.
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var cc = await FinanceSeed.CreateCostCenterAsync(client, companyId, "CC-PA", "Pending Approval CC");
        var vendor = await FinanceSeed.CreateVendorAsync(client, companyId, "Fornecedor PA", FinanceSeed.RandomCnpj());

        // Unapproved, non-cancelled payable with an overdue installment (approve: false, no cancel call).
        // Amount 400 -> should appear in Overdue1To30.
        await FinanceSeed.CreatePayableAsync(factory, client, companyId, vendor, cc, today.AddDays(-5), 400m, approve: false);

        var res = await client.GetAsync($"/api/payables/aging?companyId={companyId}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await ReadJson(res);
        var totals = body.GetProperty("totals");

        // The pending-approval installment must be included in the bucket and grand total.
        Assert.Equal(400m, totals.GetProperty("overdue1To30").GetDecimal());
        Assert.Equal(400m, totals.GetProperty("grandTotal").GetDecimal());

        // The vendor pivot must surface this payable's row.
        var byVendor = body.GetProperty("byVendor");
        Assert.Equal(1, byVendor.GetArrayLength());
        Assert.Equal(vendor, byVendor[0].GetProperty("vendorId").GetString());
        Assert.Equal(400m, byVendor[0].GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task GetAgingItems_FilteredByBucket_ReturnsOnlyThatBucket_Ordered_By_DueDate()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (companyId, _, _, _, _) = await SeedAgingAsync(factory, client, today);

        var res = await client.GetAsync($"/api/payables/aging/items?companyId={companyId}&bucket=Overdue1To30");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var items = await ReadJson(res);
        Assert.Equal(2, items.GetArrayLength()); // 200 (due -10) and remaining 50 (due -5)

        foreach (var it in items.EnumerateArray())
            Assert.Equal("Overdue1To30", it.GetProperty("bucket").GetString());

        // Ordered by DueDate ascending: due -10 first, due -5 second.
        Assert.Equal(today.AddDays(-10).ToString("yyyy-MM-dd"), items[0].GetProperty("dueDate").GetString());
        Assert.Equal(200m, items[0].GetProperty("remaining").GetDecimal());
        Assert.Equal(10, items[0].GetProperty("daysOverdue").GetInt32());

        Assert.Equal(today.AddDays(-5).ToString("yyyy-MM-dd"), items[1].GetProperty("dueDate").GetString());
        Assert.Equal(50m, items[1].GetProperty("remaining").GetDecimal());
        Assert.Equal(5, items[1].GetProperty("daysOverdue").GetInt32());
    }

    [Fact]
    public async Task GetAgingItems_FilteredByVendor_ReturnsOnlyThatVendors_OpenInstallments()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (companyId, _, vendorB, _, _) = await SeedAgingAsync(factory, client, today);

        var items = await ReadJson(await client.GetAsync($"/api/payables/aging/items?companyId={companyId}&vendorId={vendorB}"));
        Assert.Equal(2, items.GetArrayLength()); // 300 (Overdue31To60) + remaining 50 (Overdue1To30)
        foreach (var it in items.EnumerateArray())
            Assert.Equal(vendorB, it.GetProperty("vendorId").GetString());
    }
}
