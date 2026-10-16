using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Identity.Domain;
using Ulbra.One.IntegrationTests.Finance;
using Xunit;

namespace Ulbra.One.IntegrationTests.Budgeting;

[Collection("Postgres")]
public class RequisitionEndpointsTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private static async Task<(string CompanyId, string UnitId, string CostCenterId, string VendorId)> SeedDimensionAsync(HttpClient client)
    {
        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var unitId = await FinanceSeed.CreateUnitAsync(client, companyId, "Campus Teste", "CT");
        // Associate the unit with the cost center (CostCenterUnit) — the requisition create now requires it.
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, $"CC{Random.Shared.Next(100_000, 999_999)}", "Marketing", unitId);
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Teste");
        return (companyId, unitId, costCenterId, vendorId);
    }

    private static object NewRequisition(
        string unitId, string costCenterId, string vendorId, string type = "Purchase", string estimateType = "Full",
        decimal estimate = 1_000m, string? start = "2026-07", string? end = null,
        string? paymentFrequency = null, int? installments = null, string firstDueDate = "2026-07-10") =>
        new
        {
            unitId,
            costCenterId,
            vendorId,
            type,
            description = "Comprar cadeiras",
            observation = (string?)null,
            estimateValue = estimate,
            estimateType,
            startCompetency = start,
            endCompetency = end,
            firstDueDate,
            paymentFrequency,
            paymentInstallmentsCount = installments,
        };

    private static MultipartFormDataContent FileContent(byte[] bytes, string fileName, string contentType)
    {
        var content = new MultipartFormDataContent();
        var fileBytes = new ByteArrayContent(bytes);
        fileBytes.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileBytes, "file", fileName);
        return content;
    }

    private static byte[] ValidPdf() => Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n");

    [Fact]
    public async Task Anonymous_Returns_401()
    {
        var factory = fixture.Api;
        var client = factory.CreateClient();
        var res = await client.GetAsync("/api/requisitions");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Create_Get_List_Update_Delete_Flow()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var (companyId, unitId, costCenterId, vendorId) = await SeedDimensionAsync(client);

        var createRes = await client.PostAsJsonAsync("/api/requisitions", NewRequisition(unitId, costCenterId, vendorId));
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = await ReadJson(createRes);
        var id = created.GetProperty("id").GetString()!;
        Assert.Equal(companyId, created.GetProperty("companyId").GetString()); // derived from the unit
        Assert.Equal("Draft", created.GetProperty("status").GetString());
        Assert.True(created.GetProperty("code").GetInt32() >= 1);
        Assert.Equal(1_000m, created.GetProperty("totalCost").GetDecimal());

        var getRes = await client.GetAsync($"/api/requisitions/{id}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);

        var list = await ReadJson(await client.GetAsync($"/api/requisitions?unitId={unitId}"));
        var ids = list.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetString()).ToList();
        Assert.Contains(id, ids);

        var updateRes = await client.PutAsJsonAsync($"/api/requisitions/{id}", new
        {
            vendorId,
            type = "ServiceProvision",
            description = "Serviço atualizado",
            observation = "nota",
            estimateValue = 7_500m,
            estimateType = "Monthly",
            startCompetency = "2026-08",
            endCompetency = "2026-10",
            firstDueDate = "2026-08-05",
            paymentFrequency = "Monthly",
            paymentInstallmentsCount = 3,
        });
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);
        var updated = await ReadJson(updateRes);
        Assert.Equal("Serviço atualizado", updated.GetProperty("description").GetString());
        Assert.Equal(7_500m, updated.GetProperty("estimateValue").GetDecimal());
        Assert.Equal("Monthly", updated.GetProperty("estimateType").GetString());
        // EstimateValue is the TOTAL; totalCost equals it (the 3 installments of 2500 divide it).
        Assert.Equal(7_500m, updated.GetProperty("totalCost").GetDecimal());

        var deleteRes = await client.DeleteAsync($"/api/requisitions/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteRes.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/requisitions/{id}")).StatusCode);
    }

    [Fact]
    public async Task Create_Assigns_Sequential_Code_Per_Company()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionAsync(client);

        var first = await ReadJson(await client.PostAsJsonAsync("/api/requisitions", NewRequisition(unitId, costCenterId, vendorId)));
        var second = await ReadJson(await client.PostAsJsonAsync("/api/requisitions", NewRequisition(unitId, costCenterId, vendorId)));

        var code1 = first.GetProperty("code").GetInt32();
        var code2 = second.GetProperty("code").GetInt32();
        Assert.Equal(1, code1); // fresh company starts at 1
        Assert.Equal(code1 + 1, code2);
    }

    [Fact]
    public async Task Create_Unknown_Unit_Returns_400()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var (_, _, costCenterId, vendorId) = await SeedDimensionAsync(client);

        var res = await client.PostAsJsonAsync("/api/requisitions", NewRequisition($"unit_{Guid.NewGuid():N}", costCenterId, vendorId));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(ErrorCodes.Requisition.UnitNotFound, (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Create_Unknown_CostCenter_Returns_400()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var (_, unitId, _, vendorId) = await SeedDimensionAsync(client);

        var res = await client.PostAsJsonAsync("/api/requisitions", NewRequisition(unitId, $"cost_center_{Guid.NewGuid():N}", vendorId));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(ErrorCodes.Requisition.CostCenterNotFound, (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Create_Company_Mismatch_Returns_400()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyA = await FinanceSeed.CreateCompanyAsync(client);
        var companyB = await FinanceSeed.CreateCompanyAsync(client);
        var unitB = await FinanceSeed.CreateUnitAsync(client, companyB, "Campus B", "CB");
        var costCenterA = await FinanceSeed.CreateCostCenterAsync(client, companyA, $"CC{Random.Shared.Next(100_000, 999_999)}", "MKT");

        var res = await client.PostAsJsonAsync("/api/requisitions", NewRequisition(unitB, costCenterA, $"vendor_{Guid.NewGuid():N}"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(ErrorCodes.Requisition.CompanyMismatch, (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Create_Unit_Not_In_Cost_Center_Returns_400()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var (companyId, _, costCenterId, vendorId) = await SeedDimensionAsync(client);
        // A second unit in the same company, NOT associated with the cost center.
        var otherUnit = await FinanceSeed.CreateUnitAsync(client, companyId, "Campus Solto", "CS");

        var res = await client.PostAsJsonAsync("/api/requisitions", NewRequisition(otherUnit, costCenterId, vendorId));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(ErrorCodes.Requisition.UnitNotInCostCenter, (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Create_Invalid_Competency_Returns_400()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionAsync(client);

        var res = await client.PostAsJsonAsync("/api/requisitions", NewRequisition(unitId, costCenterId, vendorId,start: "2026-13"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("requisition.competency_invalid", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Create_NonPositive_Estimate_Returns_400()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionAsync(client);

        var res = await client.PostAsJsonAsync("/api/requisitions", NewRequisition(unitId, costCenterId, vendorId,estimate: 0m));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(ErrorCodes.Requisition.EstimateInvalid, (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Create_End_Before_Start_Returns_400()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionAsync(client);

        var res = await client.PostAsJsonAsync("/api/requisitions",
            NewRequisition(unitId, costCenterId, vendorId,estimateType: "Monthly", start: "2026-07", end: "2026-06"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("requisition.competency_range_invalid", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task List_Filters_By_Competency_Window()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionAsync(client);

        var created = await ReadJson(await client.PostAsJsonAsync("/api/requisitions",
            NewRequisition(unitId, costCenterId, vendorId,estimateType: "Monthly", start: "2033-03", end: "2033-09")));
        var id = created.GetProperty("id").GetString()!;

        var inWindow = await ReadJson(await client.GetAsync($"/api/requisitions?unitId={unitId}&competency=2033-06"));
        Assert.Contains(id, inWindow.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetString()));

        var outOfWindow = await ReadJson(await client.GetAsync($"/api/requisitions?unitId={unitId}&competency=2033-01"));
        Assert.DoesNotContain(id, outOfWindow.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task Update_NonExistent_Returns_404()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var res = await client.PutAsJsonAsync($"/api/requisitions/req_{Guid.NewGuid():N}", new
        {
            type = "Purchase",
            description = "x",
            estimateValue = 1m,
            estimateType = "Full",
            startCompetency = "2026-07",
        });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Delete_NonExistent_Returns_404()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var res = await client.DeleteAsync($"/api/requisitions/req_{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Create_Without_Requisitions_Permission_Returns_403()
    {
        var factory = fixture.Api;
        // A role with another permission, but without requisitions:write → the policy denies before the handler.
        var client = factory.CreateClientWith(Roles.Finance, Permissions.CoreRead);
        var res = await client.PostAsJsonAsync("/api/requisitions", new { });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Admin_Is_Not_Forbidden()
    {
        var factory = fixture.Api;
        var client = factory.CreateClientWith(Roles.Admin);
        var res = await client.PostAsJsonAsync("/api/requisitions", NewRequisition($"unit_{Guid.NewGuid():N}", $"cost_center_{Guid.NewGuid():N}", $"vendor_{Guid.NewGuid():N}"));
        Assert.NotEqual(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Upload_ValidPdf_Returns_201_And_Is_Listed()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionAsync(client);
        var id = (await ReadJson(await client.PostAsJsonAsync("/api/requisitions", NewRequisition(unitId, costCenterId, vendorId)))).GetProperty("id").GetString()!;

        using var content = FileContent(ValidPdf(), "orcamento.pdf", "application/pdf");
        var res = await client.PostAsync($"/api/requisitions/{id}/attachments", content);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.Equal("orcamento.pdf", (await ReadJson(res)).GetProperty("fileName").GetString());

        var requisition = await ReadJson(await client.GetAsync($"/api/requisitions/{id}"));
        Assert.Equal(1, requisition.GetProperty("attachments").GetArrayLength());
    }

    [Fact]
    public async Task Update_attachment_kind_reclassifies_the_file()
    {
        // AEL-959: the requester/controllership fixes a mis-typed attachment (e.g. a boleto stored as Outro).
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionAsync(client);
        var id = (await ReadJson(await client.PostAsJsonAsync("/api/requisitions", NewRequisition(unitId, costCenterId, vendorId)))).GetProperty("id").GetString()!;

        using var content = FileContent(ValidPdf(), "boleto.pdf", "application/pdf");
        var uploaded = await ReadJson(await client.PostAsync($"/api/requisitions/{id}/attachments", content));
        Assert.Equal("Other", uploaded.GetProperty("kind").GetString()); // no ?kind= → default Other
        var attachmentId = uploaded.GetProperty("id").GetString()!;

        var patched = await client.PatchAsJsonAsync($"/api/requisitions/{id}/attachments/{attachmentId}", new { kind = "BankSlip" });
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        Assert.Equal("BankSlip", (await ReadJson(patched)).GetProperty("kind").GetString());

        var detail = await ReadJson(await client.GetAsync($"/api/requisitions/{id}"));
        Assert.Equal("BankSlip", detail.GetProperty("attachments")[0].GetProperty("kind").GetString());

        var bad = await client.PatchAsJsonAsync($"/api/requisitions/{id}/attachments/{attachmentId}", new { kind = "Nope" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("attachment.invalid_kind", (await ReadJson(bad)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Upload_UnsupportedType_Returns_400()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionAsync(client);
        var id = (await ReadJson(await client.PostAsJsonAsync("/api/requisitions", NewRequisition(unitId, costCenterId, vendorId)))).GetProperty("id").GetString()!;

        var garbage = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00 }; // "MZ" PE header
        using var content = FileContent(garbage, "malware.exe", "application/octet-stream");
        var res = await client.PostAsync($"/api/requisitions/{id}/attachments", content);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("file.unsupported_type", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Download_Attachment_Returns_Stored_Bytes()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionAsync(client);
        var id = (await ReadJson(await client.PostAsJsonAsync("/api/requisitions", NewRequisition(unitId, costCenterId, vendorId)))).GetProperty("id").GetString()!;

        var pdf = ValidPdf();
        using var content = FileContent(pdf, "orcamento.pdf", "application/pdf");
        var attachmentId = (await ReadJson(await client.PostAsync($"/api/requisitions/{id}/attachments", content))).GetProperty("id").GetString()!;

        var downloadRes = await client.GetAsync($"/api/requisitions/{id}/attachments/{attachmentId}");
        Assert.Equal(HttpStatusCode.OK, downloadRes.StatusCode);
        Assert.Equal(pdf, await downloadRes.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Upload_To_NonExistent_Requisition_Returns_404()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();

        using var content = FileContent(ValidPdf(), "orcamento.pdf", "application/pdf");
        var res = await client.PostAsync($"/api/requisitions/req_{Guid.NewGuid():N}/attachments", content);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("requisition.not_found", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task List_Mine_Scopes_To_Caller()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync();
        var (companyId, unitId, costCenterId, vendorId) = await SeedDimensionAsync(admin);

        // Two distinct requesters: CreateClientWithUnitScope generates a new userId per call, and the
        // AuditInterceptor stamps CreatedBy with that id — the same string the `mine` filter compares.
        // A scoped role needs the company, the unit AND the cost center all granted to create (AEL-883,
        // explicit intersection — an empty dimension blocks), so grant the seeded trio.
        var company = CompanyId.Parse(companyId, null).Value;
        var unit = UnitId.Parse(unitId, null).Value;
        var costCenter = CostCenterId.Parse(costCenterId, null).Value;
        var alice = factory.CreateClientWithUnitScope(Roles.Requester, [company], [(company, unit)], [(company, costCenter)], Permissions.RequisitionsRead, Permissions.RequisitionsWrite);
        var bob = factory.CreateClientWithUnitScope(Roles.Requester, [company], [(company, unit)], [(company, costCenter)], Permissions.RequisitionsRead, Permissions.RequisitionsWrite);

        var aliceReq = (await ReadJson(await alice.PostAsJsonAsync("/api/requisitions", NewRequisition(unitId, costCenterId, vendorId)))).GetProperty("id").GetString()!;
        var bobReq = (await ReadJson(await bob.PostAsJsonAsync("/api/requisitions", NewRequisition(unitId, costCenterId, vendorId)))).GetProperty("id").GetString()!;

        // mine=true → each one sees only their own requisition.
        var aliceMine = await ReadJson(await alice.GetAsync($"/api/requisitions?unitId={unitId}&mine=true"));
        var aliceMineIds = aliceMine.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetString()).ToList();
        Assert.Contains(aliceReq, aliceMineIds);
        Assert.DoesNotContain(bobReq, aliceMineIds);

        // Without mine → whole-company scope (sees both).
        var all = await ReadJson(await alice.GetAsync($"/api/requisitions?unitId={unitId}"));
        var allIds = all.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetString()).ToList();
        Assert.Contains(aliceReq, allIds);
        Assert.Contains(bobReq, allIds);
    }

    // ---- Unit-scoped requester create gate (AEL-883) ----------------------------------------------

    [Fact]
    public async Task Create_Requester_Within_Scope_Returns_201()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync();
        var (companyId, unitId, costCenterId, vendorId) = await SeedDimensionAsync(admin);
        var company = CompanyId.Parse(companyId, null).Value;
        var unit = UnitId.Parse(unitId, null).Value;
        var costCenter = CostCenterId.Parse(costCenterId, null).Value;

        // The full intersection is granted: company + unit + cost center (AEL-883).
        var requester = factory.CreateClientWithUnitScope(Roles.Requester, [company], [(company, unit)], [(company, costCenter)],
            Permissions.RequisitionsRead, Permissions.RequisitionsWrite);
        var res = await requester.PostAsJsonAsync("/api/requisitions", NewRequisition(unitId, costCenterId, vendorId));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    }

    [Fact]
    public async Task Create_Requester_Outside_Scope_Returns_403()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync();
        var (companyId, unitId, costCenterId, vendorId) = await SeedDimensionAsync(admin);
        var company = CompanyId.Parse(companyId, null).Value;
        var unit = UnitId.Parse(unitId, null).Value;

        // Scoped to the company + the right unit, but the cost center dimension is a DIFFERENT one → the
        // requisition's cost center is out of scope (intersection fails; an empty/mismatched dimension blocks).
        var requester = factory.CreateClientWithUnitScope(Roles.Requester, [company], [(company, unit)], [(company, Guid.NewGuid())],
            Permissions.RequisitionsRead, Permissions.RequisitionsWrite);
        var res = await requester.PostAsJsonAsync("/api/requisitions", NewRequisition(unitId, costCenterId, vendorId));

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Contains("requisition.scope_forbidden", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Create_Requester_Without_Any_Scope_Returns_403()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync();
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionAsync(admin);

        var requester = factory.CreateClientWith(Roles.Requester, Permissions.RequisitionsRead, Permissions.RequisitionsWrite);
        var res = await requester.PostAsJsonAsync("/api/requisitions", NewRequisition(unitId, costCenterId, vendorId));

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task List_Search_By_Code_Description_And_Vendor_Name()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var unitId = await FinanceSeed.CreateUnitAsync(client, companyId, "Campus Busca", "CB");
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, $"CC{Random.Shared.Next(100_000, 999_999)}", "Marketing", unitId);
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Metalúrgica Zephyr");

        var created = await ReadJson(await client.PostAsJsonAsync("/api/requisitions", NewRequisition(unitId, costCenterId, vendorId)));
        var id = created.GetProperty("id").GetString()!;
        var code = created.GetProperty("code").GetInt32();

        // by #code (%23 = '#')
        var byCode = await ReadJson(await client.GetAsync($"/api/requisitions?unitId={unitId}&search=%23{code}"));
        Assert.Contains(id, byCode.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetString()));

        // by description substring ("cadeiras" — from NewRequisition)
        var byDesc = await ReadJson(await client.GetAsync($"/api/requisitions?unitId={unitId}&search=cadeiras"));
        Assert.Contains(id, byDesc.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetString()));

        // by vendor name (cross-module, case-insensitive)
        var byVendor = await ReadJson(await client.GetAsync($"/api/requisitions?unitId={unitId}&search=zephyr"));
        Assert.Contains(id, byVendor.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetString()));

        // non-matching term excludes it
        var none = await ReadJson(await client.GetAsync($"/api/requisitions?unitId={unitId}&search=inexistente-xyz"));
        Assert.DoesNotContain(id, none.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task List_Search_By_Vendor_Document_Digits()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var unitId = await FinanceSeed.CreateUnitAsync(client, companyId, "Campus Doc", "CD");
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, $"CC{Random.Shared.Next(100_000, 999_999)}", "Marketing", unitId);
        var cnpj = FinanceSeed.RandomCnpj();
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Documento", cnpj);
        var id = (await ReadJson(await client.PostAsJsonAsync("/api/requisitions", NewRequisition(unitId, costCenterId, vendorId)))).GetProperty("id").GetString()!;

        // Digits-only match on the vendor CNPJ (parity with the payables search).
        var byDoc = await ReadJson(await client.GetAsync($"/api/requisitions?unitId={unitId}&search={cnpj}"));
        Assert.Contains(id, byDoc.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task List_Search_Finds_Soft_Deleted_Vendor_By_Name_And_Still_Shows_It()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var unitId = await FinanceSeed.CreateUnitAsync(client, companyId, "Campus Fantasma", "CF");
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, $"CC{Random.Shared.Next(100_000, 999_999)}", "Marketing", unitId);
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Fantasma LTDA");
        var id = (await ReadJson(await client.PostAsJsonAsync("/api/requisitions", NewRequisition(unitId, costCenterId, vendorId)))).GetProperty("id").GetString()!;

        (await client.DeleteAsync($"/api/vendors/{vendorId}")).EnsureSuccessStatusCode();

        // The live requisition must stay findable by the vendor name its own row displays.
        var byName = await ReadJson(await client.GetAsync($"/api/requisitions?unitId={unitId}&search=fantasma"));
        var row = byName.GetProperty("items").EnumerateArray().FirstOrDefault(x => x.GetProperty("id").GetString() == id);
        Assert.NotEqual(JsonValueKind.Undefined, row.ValueKind);
        Assert.Equal("Fornecedor Fantasma LTDA", row.GetProperty("vendorName").GetString());
    }

    [Fact]
    public async Task List_Filters_By_Multi_Status()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionAsync(client);
        var id = (await ReadJson(await client.PostAsJsonAsync("/api/requisitions", NewRequisition(unitId, costCenterId, vendorId)))).GetProperty("id").GetString()!;

        // Created as Draft: a set that INCLUDES Draft returns it; a set that excludes Draft does not.
        var union = await ReadJson(await client.GetAsync($"/api/requisitions?unitId={unitId}&status=Draft&status=Approval"));
        Assert.Contains(id, union.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetString()));

        var excluded = await ReadJson(await client.GetAsync($"/api/requisitions?unitId={unitId}&status=Approval&status=Paid"));
        Assert.DoesNotContain(id, excluded.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task List_Filters_By_Value_Range()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionAsync(client);
        var cheap = (await ReadJson(await client.PostAsJsonAsync("/api/requisitions", NewRequisition(unitId, costCenterId, vendorId, estimate: 500m)))).GetProperty("id").GetString()!;
        var pricey = (await ReadJson(await client.PostAsJsonAsync("/api/requisitions", NewRequisition(unitId, costCenterId, vendorId, estimate: 5_000m)))).GetProperty("id").GetString()!;

        var min = await ReadJson(await client.GetAsync($"/api/requisitions?unitId={unitId}&minValue=1000"));
        var minIds = min.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetString()).ToList();
        Assert.Contains(pricey, minIds);
        Assert.DoesNotContain(cheap, minIds);

        var max = await ReadJson(await client.GetAsync($"/api/requisitions?unitId={unitId}&maxValue=1000"));
        var maxIds = max.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetString()).ToList();
        Assert.Contains(cheap, maxIds);
        Assert.DoesNotContain(pricey, maxIds);
    }

    [Fact]
    public async Task List_Sorts_By_Estimate_Ascending_And_Descending()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionAsync(client);
        foreach (var v in new[] { 300m, 100m, 200m })
            await client.PostAsJsonAsync("/api/requisitions", NewRequisition(unitId, costCenterId, vendorId, estimate: v));

        var asc = await ReadJson(await client.GetAsync($"/api/requisitions?unitId={unitId}&sort=estimate"));
        var ascVals = asc.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("estimateValue").GetDecimal()).ToList();
        Assert.Equal(new[] { 100m, 200m, 300m }, ascVals);

        var desc = await ReadJson(await client.GetAsync($"/api/requisitions?unitId={unitId}&sort=-estimate"));
        var descVals = desc.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("estimateValue").GetDecimal()).ToList();
        Assert.Equal(new[] { 300m, 200m, 100m }, descVals);
    }

    [Fact]
    public async Task List_Response_Includes_Vendor_Name()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var unitId = await FinanceSeed.CreateUnitAsync(client, companyId, "Campus Nome", "CN");
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, $"CC{Random.Shared.Next(100_000, 999_999)}", "Marketing", unitId);
        var vendorId = await FinanceSeed.CreateVendorAsync(client, "Fornecedor Nomeado LTDA");
        var id = (await ReadJson(await client.PostAsJsonAsync("/api/requisitions", NewRequisition(unitId, costCenterId, vendorId)))).GetProperty("id").GetString()!;

        var list = await ReadJson(await client.GetAsync($"/api/requisitions?unitId={unitId}"));
        var row = list.GetProperty("items").EnumerateArray().First(x => x.GetProperty("id").GetString() == id);
        Assert.Equal("Fornecedor Nomeado LTDA", row.GetProperty("vendorName").GetString());
    }

    // ---- Unit-scoped requester WRITE-PATH gate (AEL-883): every mutating endpoint honors the intersection ----

    /// <summary>An in-scope Requester (company+unit+cost center all granted) and an out-of-scope one (same
    /// company/unit but a DIFFERENT cost center → intersection fails). Both hold requisitions:write.</summary>
    private (HttpClient InScope, HttpClient OutOfScope) ScopedRequesters(string companyId, string unitId, string costCenterId)
    {
        var company = CompanyId.Parse(companyId, null).Value;
        var unit = UnitId.Parse(unitId, null).Value;
        var costCenter = CostCenterId.Parse(costCenterId, null).Value;
        var inScope = fixture.Api.CreateClientWithUnitScope(Roles.Requester, [company], [(company, unit)], [(company, costCenter)],
            Permissions.RequisitionsRead, Permissions.RequisitionsWrite);
        var outOfScope = fixture.Api.CreateClientWithUnitScope(Roles.Requester, [company], [(company, unit)], [(company, Guid.NewGuid())],
            Permissions.RequisitionsRead, Permissions.RequisitionsWrite);
        return (inScope, outOfScope);
    }

    [Fact]
    public async Task Submit_Requester_Outside_Scope_Returns_404()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync();
        var (companyId, unitId, costCenterId, vendorId) = await SeedDimensionAsync(admin);
        var id = (await ReadJson(await admin.PostAsJsonAsync("/api/requisitions", NewRequisition(unitId, costCenterId, vendorId)))).GetProperty("id").GetString()!;
        var (_, outOfScope) = ScopedRequesters(companyId, unitId, costCenterId);

        var res = await outOfScope.PostAsJsonAsync($"/api/requisitions/{id}/submit", new { });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode); // 404-hide, not 403 (no existence oracle)
    }

    [Fact]
    public async Task Submit_Requester_Within_Scope_Returns_200()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync();
        var (companyId, unitId, costCenterId, vendorId) = await SeedDimensionAsync(admin);
        var id = (await ReadJson(await admin.PostAsJsonAsync("/api/requisitions", NewRequisition(unitId, costCenterId, vendorId)))).GetProperty("id").GetString()!;
        var (inScope, _) = ScopedRequesters(companyId, unitId, costCenterId);

        var res = await inScope.PostAsJsonAsync($"/api/requisitions/{id}/submit", new { });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode); // gate passes → domain Draft→Triage
    }

    [Fact]
    public async Task Delete_Requester_Outside_Scope_Returns_404()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync();
        var (companyId, unitId, costCenterId, vendorId) = await SeedDimensionAsync(admin);
        var id = (await ReadJson(await admin.PostAsJsonAsync("/api/requisitions", NewRequisition(unitId, costCenterId, vendorId)))).GetProperty("id").GetString()!;
        var (_, outOfScope) = ScopedRequesters(companyId, unitId, costCenterId);

        var res = await outOfScope.DeleteAsync($"/api/requisitions/{id}");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        // And it is NOT actually deleted — admin still sees it.
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/requisitions/{id}")).StatusCode);
    }

    [Fact]
    public async Task Delete_Requester_Within_Scope_Returns_204()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync();
        var (companyId, unitId, costCenterId, vendorId) = await SeedDimensionAsync(admin);
        var id = (await ReadJson(await admin.PostAsJsonAsync("/api/requisitions", NewRequisition(unitId, costCenterId, vendorId)))).GetProperty("id").GetString()!;
        var (inScope, _) = ScopedRequesters(companyId, unitId, costCenterId);

        var res = await inScope.DeleteAsync($"/api/requisitions/{id}");
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
    }

    [Fact]
    public async Task Update_Requester_Outside_Scope_Returns_403()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync();
        var (companyId, unitId, costCenterId, vendorId) = await SeedDimensionAsync(admin);
        var id = (await ReadJson(await admin.PostAsJsonAsync("/api/requisitions", NewRequisition(unitId, costCenterId, vendorId)))).GetProperty("id").GetString()!;
        var (_, outOfScope) = ScopedRequesters(companyId, unitId, costCenterId);

        var res = await outOfScope.PutAsJsonAsync($"/api/requisitions/{id}", new
        {
            vendorId,
            type = "Purchase",
            description = "tentativa fora do escopo",
            estimateValue = 1_000m,
            estimateType = "Full",
            startCompetency = "2026-07",
        });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Contains("requisition.scope_forbidden", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Cancel_Requester_Outside_Scope_Returns_404()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync();
        var (companyId, unitId, costCenterId, vendorId) = await SeedDimensionAsync(admin);
        var id = (await ReadJson(await admin.PostAsJsonAsync("/api/requisitions", NewRequisition(unitId, costCenterId, vendorId)))).GetProperty("id").GetString()!;
        var (_, outOfScope) = ScopedRequesters(companyId, unitId, costCenterId);

        var res = await outOfScope.PostAsJsonAsync($"/api/requisitions/{id}/cancel", new { reason = "x" });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode); // scope gate runs before the domain state check
    }

    [Fact]
    public async Task SubmitFinancialComplement_Requester_Outside_Scope_Returns_404()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync();
        var (companyId, unitId, costCenterId, vendorId) = await SeedDimensionAsync(admin);
        var id = (await ReadJson(await admin.PostAsJsonAsync("/api/requisitions", NewRequisition(unitId, costCenterId, vendorId)))).GetProperty("id").GetString()!;
        var (_, outOfScope) = ScopedRequesters(companyId, unitId, costCenterId);

        var res = await outOfScope.PostAsJsonAsync($"/api/requisitions/{id}/submit-financial-complement", new
        {
            issueDate = "2026-07-05",
            installments = new[] { new { dueDate = "2026-07-10", amount = 1_000m, paymentMethod = "Pix" } },
        });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task UploadAttachment_Requester_Outside_Scope_Returns_404()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync();
        var (companyId, unitId, costCenterId, vendorId) = await SeedDimensionAsync(admin);
        var id = (await ReadJson(await admin.PostAsJsonAsync("/api/requisitions", NewRequisition(unitId, costCenterId, vendorId)))).GetProperty("id").GetString()!;
        var (_, outOfScope) = ScopedRequesters(companyId, unitId, costCenterId);

        using var content = FileContent(ValidPdf(), "orcamento.pdf", "application/pdf");
        var res = await outOfScope.PostAsync($"/api/requisitions/{id}/attachments", content);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode); // upgraded from company-only CanAccess to the intersection
    }

    [Fact]
    public async Task Write_NonScoped_Role_Bypasses_Scope_Returns_201()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync();
        var (_, unitId, costCenterId, vendorId) = await SeedDimensionAsync(admin);

        // Finance holds requisitions:write but is NOT a scoped role (no uscope/cscope claims): it must NOT be
        // blocked by the intersection — it creates in any unit/cost center (locked decision: only Requester/
        // Approver are scope-restricted). Proves the bypass end-to-end, not just at the unit level.
        var finance = factory.CreateClientWith(Roles.Finance, Permissions.RequisitionsRead, Permissions.RequisitionsWrite);
        var res = await finance.PostAsJsonAsync("/api/requisitions", NewRequisition(unitId, costCenterId, vendorId));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    }

    // ---- Unit-scoped requester READ gate (AEL-883): list + detail are scoped server-side ----------

    [Fact]
    public async Task List_Requester_Is_Scoped_Server_Side_Without_Params()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync();
        var (companyId, unitId, costCenterId, vendorId) = await SeedDimensionAsync(admin);
        var id = (await ReadJson(await admin.PostAsJsonAsync("/api/requisitions", NewRequisition(unitId, costCenterId, vendorId)))).GetProperty("id").GetString()!;
        var (inScope, outOfScope) = ScopedRequesters(companyId, unitId, costCenterId);

        // No query params → the scope filter still applies (it is enforcement, not a client-controlled filter).
        var inIds = (await ReadJson(await inScope.GetAsync("/api/requisitions"))).GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetString()).ToList();
        Assert.Contains(id, inIds);
        var outIds = (await ReadJson(await outOfScope.GetAsync("/api/requisitions"))).GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetString()).ToList();
        Assert.DoesNotContain(id, outIds); // out-of-scope cost center → the row is not returned
    }

    [Fact]
    public async Task Get_Requester_Outside_Scope_Returns_404()
    {
        var factory = fixture.Api;
        var admin = await factory.CreateAdminClientAsync();
        var (companyId, unitId, costCenterId, vendorId) = await SeedDimensionAsync(admin);
        var id = (await ReadJson(await admin.PostAsJsonAsync("/api/requisitions", NewRequisition(unitId, costCenterId, vendorId)))).GetProperty("id").GetString()!;
        var (inScope, outOfScope) = ScopedRequesters(companyId, unitId, costCenterId);

        Assert.Equal(HttpStatusCode.NotFound, (await outOfScope.GetAsync($"/api/requisitions/{id}")).StatusCode); // 404-hide
        Assert.Equal(HttpStatusCode.OK, (await inScope.GetAsync($"/api/requisitions/{id}")).StatusCode);
    }
}
