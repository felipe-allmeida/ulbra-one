using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ulbra.One.IntegrationTests;

namespace Ulbra.One.IntegrationTests.Finance;

[Collection("Postgres")]
public class PayableAttachmentTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private sealed record Seed(string CompanyId, string CostCenterId, string VendorId);

    private static async Task<Seed> SeedAsync(HttpClient client)
    {
        var cnpj = TestDocuments.RandomCnpj();
        var companyRes = await client.PostAsJsonAsync("/api/companies", new { legalName = "ULBRA Teste", cnpj });
        companyRes.EnsureSuccessStatusCode();
        var companyId = (await ReadJson(companyRes)).GetProperty("id").GetString()!;

        var ccRes = await client.PostAsJsonAsync("/api/cost-centers",
            new { companyId, code = "CC1", name = "Marketing", annualBudget = 1_000_000m, autoApprovalLimit = 0m, unitIds = Array.Empty<string>() });
        ccRes.EnsureSuccessStatusCode();
        var costCenterId = (await ReadJson(ccRes)).GetProperty("id").GetString()!;

        var vendorRes = await client.PostAsJsonAsync("/api/vendors", new
        {
            companyId,
            personType = 1,
            name = "Fornecedor Sul Ltda",
            tradeName = "Fornecedor Sul",
            document = TestCnpj.New(),
            email = "contato@fs.com",
            phone = "5133334444",
            address = (object?)null,
            paymentTerms = "30 dias",
            bankInfo = (string?)null,
        });
        vendorRes.EnsureSuccessStatusCode();
        var vendorId = (await ReadJson(vendorRes)).GetProperty("id").GetString()!;

        return new Seed(companyId, costCenterId, vendorId);
    }

    private static async Task<string> CreatePayableAsync(HttpClient client, Seed seed)
    {
        var res = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Payable com anexo",
            installments = new[] { new { dueDate = "2026-07-10", amount = 1500m } },
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await ReadJson(res)).GetProperty("id").GetString()!;
    }

    private static MultipartFormDataContent FileContent(byte[] bytes, string fileName, string contentType)
    {
        var content = new MultipartFormDataContent();
        var fileBytes = new ByteArrayContent(bytes);
        fileBytes.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileBytes, "file", fileName);
        return content;
    }

    private static byte[] ValidPdf()
    {
        // Minimal but signature-valid PDF (starts with %PDF-1.4).
        return Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n");
    }

    [Fact]
    public async Task Upload_ValidPdf_Returns_201_And_Is_Listed_On_Payable()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var payableId = await CreatePayableAsync(client, seed);

        using var content = FileContent(ValidPdf(), "nota.pdf", "application/pdf");
        var res = await client.PostAsync($"/api/payables/{payableId}/attachments", content);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var body = await ReadJson(res);
        Assert.Equal("nota.pdf", body.GetProperty("fileName").GetString());
        Assert.Equal("application/pdf", body.GetProperty("contentType").GetString());

        var getRes = await client.GetAsync($"/api/payables/{payableId}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var payable = await ReadJson(getRes);
        Assert.Equal(1, payable.GetProperty("attachments").GetArrayLength());
    }

    [Fact]
    public async Task Upload_Oversized_Returns_400_TooLarge()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var payableId = await CreatePayableAsync(client, seed);

        var big = new byte[26 * 1024 * 1024];
        // Make it a valid PDF header so size is the failing rule.
        Encoding.ASCII.GetBytes("%PDF-1.4\n").CopyTo(big, 0);

        using var content = FileContent(big, "grande.pdf", "application/pdf");
        var res = await client.PostAsync($"/api/payables/{payableId}/attachments", content);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("file.too_large", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Upload_UnsupportedType_Returns_400_UnsupportedType()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var payableId = await CreatePayableAsync(client, seed);

        var garbage = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00 }; // "MZ" PE header
        using var content = FileContent(garbage, "malware.exe", "application/octet-stream");
        var res = await client.PostAsync($"/api/payables/{payableId}/attachments", content);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("file.unsupported_type", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Download_Returns_Stored_Bytes()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var payableId = await CreatePayableAsync(client, seed);

        var pdf = ValidPdf();
        using var content = FileContent(pdf, "nota.pdf", "application/pdf");
        var uploadRes = await client.PostAsync($"/api/payables/{payableId}/attachments", content);
        Assert.Equal(HttpStatusCode.Created, uploadRes.StatusCode);
        var attachmentId = (await ReadJson(uploadRes)).GetProperty("id").GetString()!;

        var downloadRes = await client.GetAsync($"/api/payables/{payableId}/attachments/{attachmentId}");
        Assert.Equal(HttpStatusCode.OK, downloadRes.StatusCode);
        var downloaded = await downloadRes.Content.ReadAsByteArrayAsync();
        Assert.Equal(pdf, downloaded);
    }

    [Fact]
    public async Task Upload_To_NonExistent_Payable_Returns_404()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();

        using var content = FileContent(ValidPdf(), "nota.pdf", "application/pdf");
        var res = await client.PostAsync($"/api/payables/payable_{Guid.NewGuid():N}/attachments", content);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("payable.not_found", (await ReadJson(res)).GetProperty("error_code").GetString());
    }
}
