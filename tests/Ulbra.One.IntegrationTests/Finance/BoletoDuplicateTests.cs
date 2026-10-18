using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.IntegrationTests;
using Xunit;

namespace Ulbra.One.IntegrationTests.Finance;

/// <summary>
/// The same linha digitável must not be queued for payment twice: it identifies one issued boleto, so a second
/// installment carrying it means the bank is asked to pay the same slip again. The check spans modules — a
/// requisition plan and a Finance title are compared against each other — which is what
/// <c>IBoletoLineSource</c> exists for.
/// </summary>
[Collection("Postgres")]
public class BoletoDuplicateTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private static async Task<JsonElement> Ok(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static async Task<string> ErrorCodeOf(HttpResponseMessage res) =>
        (await ReadJson(res)).GetProperty("error_code").GetString()!;

    private sealed record Seed(string CompanyId, string CostCenterId, string VendorId);

    private static async Task<Seed> SeedAsync(HttpClient client)
    {
        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, "CC1", "Marketing");
        var vendorRes = await client.PostAsJsonAsync("/api/vendors", new
        {
            personType = 1,
            name = "Fornecedor Sul Ltda",
            document = TestCnpj.New(),
            email = "contato@fs.com",
            phone = "5133334444",
            paymentTerms = "30 dias",
        });
        vendorRes.EnsureSuccessStatusCode();
        return new Seed(companyId, costCenterId, (await ReadJson(vendorRes)).GetProperty("id").GetString()!);
    }

    private static Task<HttpResponseMessage> CreatePayableAsync(
        HttpClient client, Seed seed, params string?[] lines) =>
        client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Compra com boleto",
            installments = lines.Select((line, i) => new
            {
                dueDate = $"2026-07-{10 + i:00}",
                amount = 100m,
                boletoDigitableLine = line,
            }).ToArray(),
        });

    [Fact]
    public async Task A_line_already_used_by_another_title_is_refused()
    {
        var client = await fixture.Api.CreateAdminClientAsync($"dup_{Guid.NewGuid():N}@ulbra.br");
        var seed = await SeedAsync(client);
        var line = FinanceSeed.NewBoletoLine();
        await Ok(await CreatePayableAsync(client, seed, line));

        var res = await CreatePayableAsync(client, seed, line);

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal(ErrorCodes.Payable.BoletoDigitableLineDuplicate, await ErrorCodeOf(res));
    }

    [Fact]
    public async Task The_check_spans_companies_because_one_boleto_is_only_owed_once()
    {
        var client = await fixture.Api.CreateAdminClientAsync($"dup_{Guid.NewGuid():N}@ulbra.br");
        var first = await SeedAsync(client);
        var second = await SeedAsync(client);
        var line = FinanceSeed.NewBoletoLine();
        await Ok(await CreatePayableAsync(client, first, line));

        var res = await CreatePayableAsync(client, second, line);

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task A_masked_line_still_collides_with_the_stored_one()
    {
        var client = await fixture.Api.CreateAdminClientAsync($"dup_{Guid.NewGuid():N}@ulbra.br");
        var seed = await SeedAsync(client);
        var line = FinanceSeed.NewBoletoLine();
        await Ok(await CreatePayableAsync(client, seed, line));

        var masked = string.Join('.', line.Chunk(5).Select(c => new string(c)));
        var res = await CreatePayableAsync(client, seed, masked);

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task One_request_repeating_the_line_across_two_installments_is_refused()
    {
        var client = await fixture.Api.CreateAdminClientAsync($"dup_{Guid.NewGuid():N}@ulbra.br");
        var seed = await SeedAsync(client);

        var line = FinanceSeed.NewBoletoLine();
        var res = await CreatePayableAsync(client, seed, line, line);

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal(ErrorCodes.Payable.BoletoDigitableLineDuplicate, await ErrorCodeOf(res));
    }

    [Fact]
    public async Task Distinct_lines_are_allowed()
    {
        var client = await fixture.Api.CreateAdminClientAsync($"dup_{Guid.NewGuid():N}@ulbra.br");
        var seed = await SeedAsync(client);

        Assert.Equal(HttpStatusCode.Created, (await CreatePayableAsync(client, seed, FinanceSeed.NewBoletoLine(), FinanceSeed.NewBoletoLine())).StatusCode);
    }

    [Fact]
    public async Task Installments_without_a_boleto_line_never_collide()
    {
        var client = await fixture.Api.CreateAdminClientAsync($"dup_{Guid.NewGuid():N}@ulbra.br");
        var seed = await SeedAsync(client);

        Assert.Equal(HttpStatusCode.Created, (await CreatePayableAsync(client, seed, null, null)).StatusCode);
    }

    [Fact]
    public async Task Re_saving_the_same_title_with_its_own_line_is_not_a_conflict()
    {
        var client = await fixture.Api.CreateAdminClientAsync($"dup_{Guid.NewGuid():N}@ulbra.br");
        var seed = await SeedAsync(client);
        var line = FinanceSeed.NewBoletoLine();
        var created = await Ok(await CreatePayableAsync(client, seed, line));
        var id = created.GetProperty("id").GetString()!;

        var res = await client.PutAsJsonAsync($"/api/payables/{id}", new
        {
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Compra com boleto (revisada)",
            installments = new[] { new { dueDate = "2026-07-10", amount = 100m, boletoDigitableLine = line } },
        });

        Assert.True(res.IsSuccessStatusCode,
            $"HTTP {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task A_cancelled_title_frees_its_line_for_a_re_issued_boleto()
    {
        var client = await fixture.Api.CreateAdminClientAsync($"dup_{Guid.NewGuid():N}@ulbra.br");
        var seed = await SeedAsync(client);
        var line = FinanceSeed.NewBoletoLine();
        var created = await Ok(await CreatePayableAsync(client, seed, line));
        var id = created.GetProperty("id").GetString()!;
        await Ok(await client.PostAsJsonAsync($"/api/payables/{id}/cancel", new { reason = "Boleto reemitido" }));

        var res = await CreatePayableAsync(client, seed, line);

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    }

    // ---------- PIX Copia e Cola (AEL-963) ----------

    /// <summary>Dynamic BR Code: its own payload URL per cobrança, so it is a charge and repeats never.</summary>
    private const string DynamicQr =
        "00020101021226560014br.gov.bcb.pix2534pix.example.com/qr/v2/abc123def4565204000053039865802BR5912FULANO TESTE6009SAO PAULO62070503***6304D8B5";
    /// <summary>Static BR Code: the vendor's key in QR clothing — reusable, exactly like the key.</summary>
    private const string StaticQr =
        "00020101021126580014br.gov.bcb.pix0136123e4567-e89b-12d3-a456-4266141740005204000053039865802BR5912FULANO TESTE6009SAO PAULO62070503***63042690";

    private static Task<HttpResponseMessage> CreatePixPayableAsync(HttpClient client, Seed seed, params string[] payloads) =>
        client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Compra por PIX QR",
            paymentMethod = "Pix",
            installments = payloads.Select((payload, i) => new
            {
                dueDate = $"2026-07-{10 + i:00}",
                amount = 100m,
                pixPayloadEmv = payload,
            }).ToArray(),
        });

    [Fact]
    public async Task A_dynamic_pix_qr_already_used_by_another_title_is_refused()
    {
        var client = await fixture.Api.CreateAdminClientAsync($"dup_{Guid.NewGuid():N}@ulbra.br");
        var seed = await SeedAsync(client);
        await Ok(await CreatePixPayableAsync(client, seed, DynamicQr));

        var res = await CreatePixPayableAsync(client, seed, DynamicQr);

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal(ErrorCodes.Payable.PixPayloadEmvDuplicate, await ErrorCodeOf(res));
    }

    /// <summary>The regression that would hurt most: a static QR is an address. Two titles paying the same
    /// vendor by static QR is routine, and blocking it would strand legitimate payments.</summary>
    [Fact]
    public async Task A_static_pix_qr_repeats_freely_because_it_is_an_address()
    {
        var client = await fixture.Api.CreateAdminClientAsync($"dup_{Guid.NewGuid():N}@ulbra.br");
        var seed = await SeedAsync(client);
        await Ok(await CreatePixPayableAsync(client, seed, StaticQr));

        var res = await CreatePixPayableAsync(client, seed, StaticQr);

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    }

    [Fact]
    public async Task One_request_repeating_a_dynamic_qr_across_two_installments_is_refused()
    {
        var client = await fixture.Api.CreateAdminClientAsync($"dup_{Guid.NewGuid():N}@ulbra.br");
        var seed = await SeedAsync(client);

        var res = await CreatePixPayableAsync(client, seed, DynamicQr, DynamicQr);

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }
}
