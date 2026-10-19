using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ulbra.One.IntegrationTests;
using Ulbra.One.Common.Application.Authentication; // Permissions
using Ulbra.One.Common.Application.Ids; // CompanyId
using Ulbra.One.Identity.Domain; // Roles

namespace Ulbra.One.IntegrationTests.Authorization;

/// <summary>
/// Per-resource authorization (AEL-764): a user only accesses blobs of the companies it has scope for.
/// Covers the matrix anonymous→401, no permission→403, wrong company→404 (masks existence),
/// right scope→200, Admin→bypass; and the arbitrary-key/traversal vectors of the fiscal parse.
/// </summary>
[Collection("Postgres")]
public class AttachmentScopeAuthorizationTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private static byte[] ValidPdf() =>
        Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n");

    /// <summary>Seeds company/cost-center/vendor/payable and uploads one attachment (as Admin).
    /// Returns the company's raw Guid, the payable id and the attachment id (all wire forms for the ids).</summary>
    private static async Task<(Guid CompanyGuid, string PayableId, string AttachmentId, byte[] Bytes)> SeedPayableWithAttachmentAsync(HttpClient admin)
    {
        var cnpj = TestDocuments.RandomCnpj();
        var companyRes = await admin.PostAsJsonAsync("/api/companies", new { legalName = "ULBRA Escopo", cnpj });
        companyRes.EnsureSuccessStatusCode();
        var companyId = (await ReadJson(companyRes)).GetProperty("id").GetString()!;

        var ccRes = await admin.PostAsJsonAsync("/api/cost-centers",
            new { companyId, code = "CC1", name = "Marketing", annualBudget = 1_000_000m, autoApprovalLimit = 0m, unitIds = Array.Empty<string>() });
        ccRes.EnsureSuccessStatusCode();
        var costCenterId = (await ReadJson(ccRes)).GetProperty("id").GetString()!;

        var vendorRes = await admin.PostAsJsonAsync("/api/vendors", new
        {
            companyId,
            personType = 1,
            name = "Fornecedor Escopo Ltda",
            tradeName = "Fornecedor Escopo",
            document = TestCnpj.New(),
            email = "contato@fs.com",
            phone = "5133334444",
            address = (object?)null,
            paymentTerms = "30 dias",
            bankInfo = (string?)null,
        });
        vendorRes.EnsureSuccessStatusCode();
        var vendorId = (await ReadJson(vendorRes)).GetProperty("id").GetString()!;

        var payableRes = await admin.PostAsJsonAsync("/api/payables", new
        {
            companyId,
            vendorId,
            costCenterId,
            description = "Payable escopo",
            installments = new[] { new { dueDate = "2026-07-10", amount = 1500m } },
        });
        Assert.Equal(HttpStatusCode.Created, payableRes.StatusCode);
        var payableId = (await ReadJson(payableRes)).GetProperty("id").GetString()!;

        var bytes = ValidPdf();
        var content = new MultipartFormDataContent();
        var fileBytes = new ByteArrayContent(bytes);
        fileBytes.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileBytes, "file", "nota.pdf");
        var uploadRes = await admin.PostAsync($"/api/payables/{payableId}/attachments", content);
        Assert.Equal(HttpStatusCode.Created, uploadRes.StatusCode);
        var attachmentId = (await ReadJson(uploadRes)).GetProperty("id").GetString()!;

        return (CompanyId.Parse(companyId, null).Value, payableId, attachmentId, bytes);
    }

    [Fact]
    public async Task Download_attachment_anonymous_returns_401()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync();
        var (_, payableId, attachmentId, _) = await SeedPayableWithAttachmentAsync(admin);

        var anon = factory.CreateClient(); // no bearer token
        var res = await anon.GetAsync($"/api/payables/{payableId}/attachments/{attachmentId}");

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Download_attachment_without_permission_returns_403()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync();
        var (company, payableId, attachmentId, _) = await SeedPayableWithAttachmentAsync(admin);

        // Right company scope, but the role lacks payables:read → the permission gate rejects first.
        var client = factory.CreateClientWithScope(Roles.Accounting, [company], Permissions.AccountingRead);
        var res = await client.GetAsync($"/api/payables/{payableId}/attachments/{attachmentId}");

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Download_attachment_wrong_company_returns_404()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync();
        var (_, payableId, attachmentId, _) = await SeedPayableWithAttachmentAsync(admin);

        // Has payables:read, but scoped to a DIFFERENT company → indistinguishable from not-found.
        var client = factory.CreateClientWithScope(Roles.Finance, [Guid.NewGuid()], Permissions.PayablesRead);
        var res = await client.GetAsync($"/api/payables/{payableId}/attachments/{attachmentId}");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Download_attachment_right_company_returns_200_and_bytes()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync();
        var (company, payableId, attachmentId, bytes) = await SeedPayableWithAttachmentAsync(admin);

        var client = factory.CreateClientWithScope(Roles.Finance, [company], Permissions.PayablesRead);
        var res = await client.GetAsync($"/api/payables/{payableId}/attachments/{attachmentId}");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(bytes, await res.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Download_attachment_admin_bypasses_scope()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync();
        var (_, payableId, attachmentId, bytes) = await SeedPayableWithAttachmentAsync(admin);

        // A fresh Admin (no scope grants) still reads any company's blobs.
        var otherAdmin = await factory.CreateAdminClientAsync("admin2@ulbra.br", "Admin Dois");
        var res = await otherAdmin.GetAsync($"/api/payables/{payableId}/attachments/{attachmentId}");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(bytes, await res.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Parse_fiscal_document_of_unaccessible_company_returns_403()
    {
        var factory = fixture.Api;
        var scopedCompany = Guid.NewGuid();
        var otherCompany = new CompanyId(Guid.NewGuid());

        var client = factory.CreateClientWithScope(Roles.Finance, [scopedCompany], Permissions.PayablesWrite);
        var res = await client.PostAsJsonAsync(
            $"/api/payables/fiscal-document/parse?companyId={otherCompany}",
            new { key = $"fiscal/{otherCompany.Value:N}/x.pdf", fileName = "x.pdf" });

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("fiscal_document.company_forbidden", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Parse_fiscal_document_with_foreign_key_returns_404()
    {
        var factory = fixture.Api;
        var company = new CompanyId(Guid.NewGuid());

        var client = factory.CreateClientWithScope(Roles.Finance, [company.Value], Permissions.PayablesWrite);
        // Accessible company, but the key is not under this company's namespace → masked as not-found.
        var res = await client.PostAsJsonAsync(
            $"/api/payables/fiscal-document/parse?companyId={company}",
            new { key = $"fiscal/{Guid.NewGuid():N}/leaked.pdf", fileName = "leaked.pdf" });

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("fiscal_document.not_found", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Parse_fiscal_document_with_traversal_key_returns_404()
    {
        var factory = fixture.Api;
        var company = new CompanyId(Guid.NewGuid());

        var client = factory.CreateClientWithScope(Roles.Finance, [company.Value], Permissions.PayablesWrite);
        var res = await client.PostAsJsonAsync(
            $"/api/payables/fiscal-document/parse?companyId={company}",
            new { key = "../../appsettings.json", fileName = "x.pdf" });

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("fiscal_document.not_found", (await ReadJson(res)).GetProperty("error_code").GetString());
    }
}
