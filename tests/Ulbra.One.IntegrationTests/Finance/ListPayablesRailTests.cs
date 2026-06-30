using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.IntegrationTests.Finance;

/// <summary>
/// Asserts that the ListPayables endpoint computes <c>rail</c> + <c>manualReason</c> via
/// batched in-memory RemittanceEligibility evaluation (no per-row DB hits).
/// </summary>
[Collection("Postgres")]
public class ListPayablesRailTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    /// <summary>
    /// Seeds a Santander 033 default paying account (with cnabAgreementCode) for the company,
    /// a vendor with Pix-capable bank info, and a vendor with no bank info.
    /// Returns (companyPrefixedId, costCenterId, pixVendorId, noBankVendorId).
    /// </summary>
    private static async Task<(string CompanyId, string CostCenterId, string PixVendorId, string NoBankVendorId)>
        SeedAsync(ApiFactory factory, HttpClient client)
    {
        var cnpj = FinanceSeed.RandomCnpj();
        var companyRes = await client.PostAsJsonAsync("/api/companies",
            new { legalName = "Empresa ListRail Test", cnpj });
        companyRes.EnsureSuccessStatusCode();
        var companyPrefixedId = (await ReadJson(companyRes)).GetProperty("id").GetString()!;

        var ccRes = await client.PostAsJsonAsync("/api/cost-centers",
            new { companyId = companyPrefixedId, code = "CC-LR", name = "Centro ListRail", annualBudget = 1_000_000m, autoApprovalLimit = 0m, unitIds = Array.Empty<string>() });
        ccRes.EnsureSuccessStatusCode();
        var costCenterId = (await ReadJson(ccRes)).GetProperty("id").GetString()!;

        // Vendor with Pix key — will be remittance-eligible when account is Santander+agreement.
        var pixVendorRes = await client.PostAsJsonAsync("/api/vendors", new
        {
            companyId = companyPrefixedId,
            personType = 1,
            name = "Fornecedor Pix Rail",
            tradeName = "FornecedorPixRail",
            document = FinanceSeed.RandomCnpj(),
            email = "pix@rail.com",
            phone = "5133334444",
            address = (object?)null,
            paymentTerms = "à vista",
            bankInfo = new { bankCode = "", branch = "", account = "", accountDigit = "", pixKey = "pix@rail.com" },
        });
        pixVendorRes.EnsureSuccessStatusCode();
        var pixVendorId = (await ReadJson(pixVendorRes)).GetProperty("id").GetString()!;

        // Vendor with no bank info — VendorBankInfoMissing.
        var noBankVendorRes = await client.PostAsJsonAsync("/api/vendors", new
        {
            companyId = companyPrefixedId,
            personType = 1,
            name = "Fornecedor SemBanco Rail",
            tradeName = "SemBancoRail",
            document = FinanceSeed.RandomCnpj(),
            email = "nobanco@rail.com",
            phone = "5133335555",
            address = (object?)null,
            paymentTerms = "30 dias",
            bankInfo = (object?)null,
        });
        noBankVendorRes.EnsureSuccessStatusCode();
        var noBankVendorId = (await ReadJson(noBankVendorRes)).GetProperty("id").GetString()!;

        // Santander 033 default paying account with agreement code + Pix as default method.
        var companyGuid = Guid.Parse(companyPrefixedId[(companyPrefixedId.LastIndexOf('_') + 1)..]);
        await using var scope = factory.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var bankAccount = new BankAccount(companyGuid, "033", "Banco Santander", "1234",
            "99887766", "1", BankAccountType.Checking, "Conta Rail Test", null,
            cnabAgreementCode: "RAILTEST12345");
        bankAccount.SetAsDefaultPayer(PaymentMethod.Pix);
        await uow.AddAsync(bankAccount, CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);

        return (companyPrefixedId, costCenterId, pixVendorId, noBankVendorId);
    }

    [Fact]
    public async Task ListPayables_Pix_Vendor_With_Santander_Account_Returns_Rail_Remittance()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var (companyId, costCenterId, pixVendorId, _) = await SeedAsync(factory, client);

        // Create + approve a payable for the Pix-capable vendor with paymentMethod=Pix.
        var createRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId,
            vendorId = pixVendorId,
            costCenterId,
            description = "Pix rail test",
            installments = new[] { new { dueDate = "2026-07-10", amount = 1_000m } },
            paymentMethod = "Pix",
        });
        createRes.EnsureSuccessStatusCode();
        var payableId = (await ReadJson(createRes)).GetProperty("id").GetString()!;
        (await client.PostAsJsonAsync($"/api/payables/{payableId}/approve", new { })).EnsureSuccessStatusCode();

        var listRes = await client.GetAsync($"/api/payables?companyId={companyId}&status=Scheduled");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var items = (await ReadJson(listRes)).GetProperty("items");
        Assert.True(items.GetArrayLength() > 0, "Expected at least one item in list");

        var item = items.EnumerateArray().First(i => i.GetProperty("payableId").GetString() == payableId);
        Assert.Equal("Remittance", item.GetProperty("rail").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("manualReason").ValueKind);
    }

    [Fact]
    public async Task ListPayables_Vendor_Without_Bank_Info_Returns_Rail_Manual_VendorBankInfoMissing()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var (companyId, costCenterId, _, noBankVendorId) = await SeedAsync(factory, client);

        // Create + approve a payable for the vendor with no bank info.
        var createRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId,
            vendorId = noBankVendorId,
            costCenterId,
            description = "No bank info rail test",
            installments = new[] { new { dueDate = "2026-07-15", amount = 500m } },
        });
        createRes.EnsureSuccessStatusCode();
        var payableId = (await ReadJson(createRes)).GetProperty("id").GetString()!;
        (await client.PostAsJsonAsync($"/api/payables/{payableId}/approve", new { })).EnsureSuccessStatusCode();

        var listRes = await client.GetAsync($"/api/payables?companyId={companyId}&status=Scheduled");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var items = (await ReadJson(listRes)).GetProperty("items");
        Assert.True(items.GetArrayLength() > 0, "Expected at least one item in list");

        var item = items.EnumerateArray().First(i => i.GetProperty("payableId").GetString() == payableId);
        Assert.Equal("Manual", item.GetProperty("rail").GetString());
        Assert.Equal("VendorBankInfoMissing", item.GetProperty("manualReason").GetString());
    }
}
