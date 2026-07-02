using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ulbra.One.IntegrationTests.Finance;

/// <summary>
/// Integration tests for PUT /api/payables/{id}/withholdings (SetWithholdings endpoint, AEL-619).
/// </summary>
[Collection("Postgres")]
public class SetWithholdingsEndpointTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private sealed record Seed(string CompanyId, string CostCenterId, string VendorId);

    private static async Task<Seed> SeedAsync(HttpClient client)
    {
        var cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        var companyRes = await client.PostAsJsonAsync("/api/companies",
            new { legalName = "Empresa Retencao Ltda", cnpj });
        companyRes.EnsureSuccessStatusCode();
        var companyId = (await ReadJson(companyRes)).GetProperty("id").GetString()!;

        var ccRes = await client.PostAsJsonAsync("/api/cost-centers",
            new { companyId, code = "CC-RET", name = "Retencoes", annualBudget = 1_000_000m, autoApprovalLimit = 0m, unitIds = Array.Empty<string>() });
        ccRes.EnsureSuccessStatusCode();
        var costCenterId = (await ReadJson(ccRes)).GetProperty("id").GetString()!;

        var vendorRes = await client.PostAsJsonAsync("/api/vendors", new
        {
            companyId,
            personType = 1,
            name = "Fornecedor Retencoes Ltda",
            tradeName = "Fornecedor Ret",
            document = "11.222.333/0001-81",
            email = "ret@fs.com",
            phone = "5133334444",
            address = (object?)null,
            paymentTerms = "30 dias",
            bankInfo = (string?)null,
        });
        vendorRes.EnsureSuccessStatusCode();
        var vendorId = (await ReadJson(vendorRes)).GetProperty("id").GetString()!;

        return new Seed(companyId, costCenterId, vendorId);
    }

    private static async Task<string> CreatePayableAsync(HttpClient client, Seed seed, decimal amount)
    {
        var res = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Servico para retencao",
            installments = new[] { new { dueDate = "2026-09-01", amount } },
            type = "Product", // product here (test drives withholdings via the manual endpoint) — lets it schedule
        });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("id").GetString()!;
    }

    // ── Case 1: Happy path ───────────────────────────────────────────────────────

    [Fact]
    public async Task SetWithholdings_ValidIrrf_Returns200_And_GetPayable_ShowsWithholdingsAndNetFigures()
    {
        // Gross 1000, IRRF 150 → totalWithheld=150, netTotal=850, installment netAmount=850
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var payableId = await CreatePayableAsync(client, seed, 1000m);

        var putRes = await client.PutAsJsonAsync($"/api/payables/{payableId}/withholdings", new
        {
            items = new[]
            {
                new { taxType = "Irrf", baseAmount = 1000m, rate = 0.015m, amount = 150m }
            }
        });
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);

        // Verify the PUT response itself carries the withholdings + net figures.
        var putBody = await ReadJson(putRes);
        Assert.Equal(150m, putBody.GetProperty("totalWithheld").GetDecimal());
        Assert.Equal(850m, putBody.GetProperty("netTotal").GetDecimal());
        var withholdings = putBody.GetProperty("withholdings");
        Assert.Equal(1, withholdings.GetArrayLength());
        Assert.Equal("Irrf", withholdings[0].GetProperty("taxType").GetString());
        Assert.Equal(150m, withholdings[0].GetProperty("amount").GetDecimal());

        // Re-fetch via GET and validate installments' netAmount.
        var getBody = await ReadJson(await client.GetAsync($"/api/payables/{payableId}"));
        Assert.Equal(150m, getBody.GetProperty("totalWithheld").GetDecimal());
        Assert.Equal(850m, getBody.GetProperty("netTotal").GetDecimal());

        var installments = getBody.GetProperty("installments");
        Assert.Equal(1, installments.GetArrayLength());
        // Single installment: netAmount must equal netTotal (850).
        Assert.Equal(850m, installments[0].GetProperty("netAmount").GetDecimal());

        // Σ netAmount across all installments must equal netTotal.
        var sumNet = Enumerable.Range(0, installments.GetArrayLength())
            .Sum(i => installments[i].GetProperty("netAmount").GetDecimal());
        Assert.Equal(850m, sumNet);
    }

    // ── Case 2: Σ amount >= gross → 400 ─────────────────────────────────────────

    [Fact]
    public async Task SetWithholdings_TotalExceedsOrEqualsGross_Returns400_WithholdingsInvalid()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var payableId = await CreatePayableAsync(client, seed, 500m);

        // Withholding of exactly 500 (= gross) must be rejected.
        var res = await client.PutAsJsonAsync($"/api/payables/{payableId}/withholdings", new
        {
            items = new[]
            {
                new { taxType = "Iss", baseAmount = 500m, rate = (decimal?)null, amount = 500m }
            }
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Payable.WithholdingsInvalid", (await ReadJson(res)).GetProperty("title").GetString());
    }

    // ── Case 3: Clear withholdings with empty items list ─────────────────────────

    [Fact]
    public async Task SetWithholdings_EmptyItems_ClearsWithholdings_And_RestoresGrossNetAmount()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var payableId = await CreatePayableAsync(client, seed, 800m);

        // First set a withholding so there is something to clear.
        var setRes = await client.PutAsJsonAsync($"/api/payables/{payableId}/withholdings", new
        {
            items = new[] { new { taxType = "Inss", baseAmount = 800m, rate = (decimal?)null, amount = 80m } }
        });
        Assert.Equal(HttpStatusCode.OK, setRes.StatusCode);

        // Now clear by sending empty list.
        var clearRes = await client.PutAsJsonAsync($"/api/payables/{payableId}/withholdings", new
        {
            items = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.OK, clearRes.StatusCode);

        var body = await ReadJson(clearRes);
        Assert.Equal(0m, body.GetProperty("totalWithheld").GetDecimal());
        Assert.Equal(800m, body.GetProperty("netTotal").GetDecimal());
        Assert.Equal(0, body.GetProperty("withholdings").GetArrayLength());

        // Installment netAmount must be restored to the gross amount.
        var installment = body.GetProperty("installments")[0];
        Assert.Equal(800m, installment.GetProperty("netAmount").GetDecimal());
    }

    // ── Case 4: Anonymous → 401 ─────────────────────────────────────────────────

    [Fact]
    public async Task SetWithholdings_Anonymous_Returns401()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        // Use a plain HttpClient without auth headers.
        var anonClient = factory.CreateClient();

        // A fake payable id is enough — auth check fires first.
        var fakeId = $"payable_{Guid.NewGuid():N}";
        var res = await anonClient.PutAsJsonAsync($"/api/payables/{fakeId}/withholdings", new
        {
            items = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    // ── Case 5: Has-payment guard → skipped (unit-covered in Task 1) ─────────────
    // Setting up a fully paid payable through the HTTP API requires bank account + payment
    // registration and is already covered by PaymentEndpointsTests.
    // The guard is exercised by the domain's SetWithholdings() which throws
    // InvalidOperationException when non-reversed payments exist (Task 1 unit tests cover this).
    // The endpoint maps it → 400 "Payable.HasPayments" before delegating to the domain,
    // mirroring the UpdatePayable guard; this path is NOT duplicated here.

    // ── Case 6: Scheduled payable → withholdings locked (AEL-631 hardening) ───────

    [Fact]
    public async Task SetWithholdings_OnScheduledPayable_Returns400_WithholdingsLocked()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var payableId = await CreatePayableAsync(client, seed, 1000m);

        // Approve → Scheduled (the accounting incurral fires at this point).
        var approveRes = await client.PostAsJsonAsync($"/api/payables/{payableId}/approve", new { });
        approveRes.EnsureSuccessStatusCode();

        // Changing withholdings after scheduling is rejected — defense-in-depth; the UI already
        // renders the retenções section read-only once the payable leaves the pre-approval states.
        var putRes = await client.PutAsJsonAsync($"/api/payables/{payableId}/withholdings", new
        {
            items = new[]
            {
                new { taxType = "Irrf", baseAmount = 1000m, rate = 0.015m, amount = 150m }
            }
        });
        Assert.Equal(HttpStatusCode.BadRequest, putRes.StatusCode);
        Assert.Equal("Payable.WithholdingsInvalid", (await ReadJson(putRes)).GetProperty("title").GetString());
    }
}
