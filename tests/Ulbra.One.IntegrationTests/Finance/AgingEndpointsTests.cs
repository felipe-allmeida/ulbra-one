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

    private static async Task<string> CreateCompanyAsync(HttpClient client)
    {
        var cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        var res = await client.PostAsJsonAsync("/api/companies", new { legalName = "ULBRA Teste", cnpj });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("id").GetString()!;
    }

    private static async Task<string> CreateCostCenterAsync(HttpClient client, string companyId, string code, string name)
    {
        var res = await client.PostAsJsonAsync("/api/cost-centers",
            new { companyId, code, name, annualBudget = 1_000_000m, autoApprovalLimit = 1_000_000m, unitIds = Array.Empty<string>() });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("id").GetString()!;
    }

    /// <summary>Generates a valid (check-digit) CNPJ from a random 12-digit base.</summary>
    private static string RandomCnpj()
    {
        var digits = new int[14];
        for (var i = 0; i < 12; i++) digits[i] = Random.Shared.Next(0, 10);
        int[] w1 = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        int[] w2 = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        digits[12] = Dv(digits, w1);
        digits[13] = Dv(digits, w2);
        return string.Concat(digits);

        static int Dv(int[] d, int[] w)
        {
            var sum = 0;
            for (var i = 0; i < w.Length; i++) sum += d[i] * w[i];
            var rest = sum % 11;
            return rest < 2 ? 0 : 11 - rest;
        }
    }

    private static async Task<string> CreateVendorAsync(HttpClient client, string companyId, string name, string document)
    {
        var res = await client.PostAsJsonAsync("/api/vendors", new
        {
            companyId,
            personType = 1,
            name,
            tradeName = name,
            document,
            email = "contato@fs.com",
            phone = "5133334444",
            address = (object?)null,
            paymentTerms = "30 dias",
            bankInfo = (string?)null,
        });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("id").GetString()!;
    }

    private static async Task<string> CreateBankAccountAsync(HttpClient client, string companyId, string accountNumber)
    {
        var res = await client.PostAsJsonAsync("/api/bank-accounts", new
        {
            companyId,
            bankCode = "033",
            bankName = "Banco Santander",
            agency = "1234",
            accountNumber,
            accountDigit = "6",
            accountType = "Checking",
            alias = "Conta " + accountNumber,
            openingBalance = 1_000_000m,
        });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("id").GetString()!;
    }

    /// <summary>Creates a single-installment payable and (optionally) approves it. Returns (payableId, installmentId).</summary>
    private static async Task<(string PayableId, string InstallmentId)> CreatePayableAsync(
        HttpClient client, string companyId, string vendorId, string costCenterId, DateOnly dueDate, decimal amount, bool approve = true)
    {
        var createRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId,
            vendorId,
            costCenterId,
            description = "Para pagar",
            installments = new[] { new { dueDate = dueDate.ToString("yyyy-MM-dd"), amount } },
        });
        createRes.EnsureSuccessStatusCode();
        var body = await ReadJson(createRes);
        var payableId = body.GetProperty("id").GetString()!;
        var installmentId = body.GetProperty("installments")[0].GetProperty("id").GetString()!;

        if (approve)
        {
            var approveRes = await client.PostAsJsonAsync($"/api/payables/{payableId}/approve", new { });
            approveRes.EnsureSuccessStatusCode();
        }
        return (payableId, installmentId);
    }

    private static async Task RegisterPaymentAsync(HttpClient client, string payableId, string installmentId, string bankId, decimal amount, DateOnly date)
    {
        var res = await client.PostAsJsonAsync($"/api/payables/{payableId}/installments/{installmentId}/payments", new
        {
            paymentDate = date.ToString("yyyy-MM-dd"),
            amount,
            bankAccountId = bankId,
            method = "Pix",
        });
        res.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Seeds one company with two vendors / two cost centers and a mix of installments across buckets:
    /// vendor A / CC1: NotYetDue 100, Overdue1To30 200; vendor B / CC2: Overdue31To60 300, partial (remaining 50 in 1-30);
    /// plus an excluded fully-paid installment and an excluded cancelled payable.
    /// </summary>
    private async Task<(string CompanyId, string VendorA, string VendorB, string Cc1, string Cc2)> SeedAgingAsync(HttpClient client, DateOnly today)
    {
        var companyId = await CreateCompanyAsync(client);
        var cc1 = await CreateCostCenterAsync(client, companyId, "CC1", "Marketing");
        var cc2 = await CreateCostCenterAsync(client, companyId, "CC2", "TI");
        var vendorA = await CreateVendorAsync(client, companyId, "Fornecedor A", RandomCnpj());
        var vendorB = await CreateVendorAsync(client, companyId, "Fornecedor B", RandomCnpj());
        var bankId = await CreateBankAccountAsync(client, companyId, "00012345");

        // Vendor A / CC1
        await CreatePayableAsync(client, companyId, vendorA, cc1, today.AddDays(10), 100m);   // NotYetDue
        await CreatePayableAsync(client, companyId, vendorA, cc1, today.AddDays(-10), 200m);   // Overdue1To30

        // Vendor B / CC2
        await CreatePayableAsync(client, companyId, vendorB, cc2, today.AddDays(-45), 300m);   // Overdue31To60

        // Vendor B / CC2 partial: amount 80, pay 30, remaining 50 -> Overdue1To30
        var (partialPayable, partialInst) = await CreatePayableAsync(client, companyId, vendorB, cc2, today.AddDays(-5), 80m);
        await RegisterPaymentAsync(client, partialPayable, partialInst, bankId, 30m, today);

        // Excluded: fully paid installment (vendor A / CC1)
        var (paidPayable, paidInst) = await CreatePayableAsync(client, companyId, vendorA, cc1, today.AddDays(-3), 500m);
        await RegisterPaymentAsync(client, paidPayable, paidInst, bankId, 500m, today);

        // Excluded: cancelled payable (vendor A / CC1)
        var (cancelledPayable, _) = await CreatePayableAsync(client, companyId, vendorA, cc1, today.AddDays(-20), 999m, approve: false);
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
        var (companyId, _, _, _, _) = await SeedAgingAsync(client, today);

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
        var (companyId, vendorA, vendorB, _, _) = await SeedAgingAsync(client, today);

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
    public async Task GetAgingReport_ByCostCenter_HasRow_Per_CostCenter_IdOnly()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (companyId, _, _, cc1, cc2) = await SeedAgingAsync(client, today);

        var body = await ReadJson(await client.GetAsync($"/api/payables/aging?companyId={companyId}"));
        var byCostCenter = body.GetProperty("byCostCenter");
        Assert.Equal(2, byCostCenter.GetArrayLength());

        var cc2Row = byCostCenter.EnumerateArray().Single(r => r.GetProperty("costCenterId").GetString() == cc2);
        Assert.Equal(350m, cc2Row.GetProperty("total").GetDecimal());
        Assert.False(cc2Row.TryGetProperty("name", out _));

        var cc1Row = byCostCenter.EnumerateArray().Single(r => r.GetProperty("costCenterId").GetString() == cc1);
        Assert.Equal(300m, cc1Row.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task GetAgingItems_FilteredByBucket_ReturnsOnlyThatBucket_Ordered_By_DueDate()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (companyId, _, _, _, _) = await SeedAgingAsync(client, today);

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
        var (companyId, _, vendorB, _, _) = await SeedAgingAsync(client, today);

        var items = await ReadJson(await client.GetAsync($"/api/payables/aging/items?companyId={companyId}&vendorId={vendorB}"));
        Assert.Equal(2, items.GetArrayLength()); // 300 (Overdue31To60) + remaining 50 (Overdue1To30)
        foreach (var it in items.EnumerateArray())
            Assert.Equal(vendorB, it.GetProperty("vendorId").GetString());
    }
}
