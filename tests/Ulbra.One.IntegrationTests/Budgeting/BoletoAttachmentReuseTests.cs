using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Budgeting.Application.FiscalDocuments;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Storage;
using Ulbra.One.IntegrationTests.Finance;
using Ulbra.One.Persistence;
using Xunit;

namespace Ulbra.One.IntegrationTests.Budgeting;

/// <summary>
/// AEL-938 — the boleto PDF read for the fields + AI analysis and the requisition attachment are ONE single blob.
/// The reader uploads the file once (presigned PUT), <c>parse-boleto</c> reads that key, and
/// <c>attachments/from-key</c> registers the SAME key — no second upload. This guards against the earlier defect
/// where the attachment re-uploaded the bytes into a second blob (leaving the parse blob orphaned).
/// The decisive assertion: the stored attachment's <c>StoragePath</c> equals the exact key parse-boleto consumed
/// (and carries the boleto namespace prefix), which a fresh multipart <c>SaveAsync</c> re-upload never would.
/// </summary>
[Collection("Postgres")]
public class BoletoAttachmentReuseTests(PostgresFixture fixture)
{
    private static Guid Raw(string typedId) => Guid.ParseExact(typedId[(typedId.LastIndexOf('_') + 1)..], "N");

    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Boleto_parse_and_attachment_reuse_one_single_blob()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync($"boleto_{Guid.NewGuid():N}@ulbra.br");

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var unitId = await FinanceSeed.CreateUnitAsync(client, companyId, "Campus Teste", "CT");
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(
            client, companyId, $"CC{Random.Shared.Next(100_000, 999_999)}", "MKT", unitId);
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Teste");
        await client.PostAsJsonAsync($"/api/cost-centers/{costCenterId}/unit-budgets", new
        {
            unitId,
            startCompetency = "2026-07",
            endCompetency = "2026-12",
            monthlyLimit = 1_000_000m,
        });

        // A plain requisition is enough — the attachment is independent of the installment method (Pix avoids the
        // Boleto-requires-a-linha-digitável guard, which is irrelevant here).
        var createRes = await client.PostAsJsonAsync("/api/requisitions", new
        {
            unitId,
            costCenterId,
            vendorId,
            type = "Purchase",
            description = "Boleto como anexo",
            observation = (string?)null,
            estimateValue = 1_000m,
            estimateType = "Full",
            startCompetency = "2026-07",
            endCompetency = (string?)null,
            firstDueDate = "2026-07-10",
            paymentFrequency = (string?)null,
            paymentInstallmentsCount = (int?)null,
            issueDate = "2026-07-05",
            installments = new object[] { new { dueDate = "2026-07-10", amount = 1_000m, paymentMethod = "Pix" } },
        });
        createRes.EnsureSuccessStatusCode();
        var reqTypedId = (await ReadJson(createRes)).GetProperty("id").GetString()!;

        // Step 1 — the ONE and only upload: the boleto lands once in the company's boleto namespace, exactly as the
        // reader's presigned PUT would. Capture its storage key.
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n% boleto de teste\n%%EOF\n");
        var boletoPrefix = ScopedBlobKey.PrefixFor(RequisitionBoletoBlob.KeyRoot, Raw(companyId));
        string key;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
            key = await storage.SaveAsync("boleto.pdf", new MemoryStream(pdf), boletoPrefix);
        }
        Assert.StartsWith(boletoPrefix, key);

        // Step 2 — the AI-analysis read: parse-boleto consumes THAT key (Fake AI in tests → empty result, still 200).
        var parseRes = await client.PostAsJsonAsync(
            $"/api/requisitions/fiscal-document/parse-boleto?companyId={companyId}",
            new { key, fileName = "boleto.pdf" });
        Assert.Equal(HttpStatusCode.OK, parseRes.StatusCode);

        // Step 3 — attach BY KEY: reuses the same blob (no re-upload).
        var attachRes = await client.PostAsJsonAsync(
            $"/api/requisitions/{reqTypedId}/attachments/from-key",
            new { key, fileName = "boleto.pdf", contentType = "application/pdf", sizeBytes = (long)pdf.Length, kind = "Other" });
        Assert.Equal(HttpStatusCode.Created, attachRes.StatusCode);
        var attachmentId = (await ReadJson(attachRes)).GetProperty("id").GetString()!;

        // Decisive proof of a SINGLE blob: the persisted attachment points at the exact key parse-boleto read — not
        // a fresh SaveAsync key (the old re-upload produced one WITHOUT the boleto prefix).
        await using (var verify = factory.Services.CreateAsyncScope())
        {
            var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
            var attachments = await db.Set<RequisitionAttachment>()
                .Where(a => a.RequisitionId == Raw(reqTypedId)).ToListAsync();
            var attachment = Assert.Single(attachments);
            Assert.Equal(key, attachment.StoragePath);
            Assert.StartsWith(RequisitionBoletoBlob.KeyRoot + "/", attachment.StoragePath);
        }

        // And it downloads the very bytes that single blob holds.
        var download = await client.GetAsync($"/api/requisitions/{reqTypedId}/attachments/{attachmentId}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(pdf, await download.Content.ReadAsByteArrayAsync());
    }
}
