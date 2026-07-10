using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.IntegrationTests.Finance;

/// <summary>
/// Verifies the hold/release installment endpoints:
/// POST /api/payables/{id}/installments/{installmentId}/hold
/// POST /api/payables/{id}/installments/{installmentId}/release-hold
/// </summary>
[Collection("Postgres")]
public class InstallmentHoldEndpointTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    // ── 1. Hold an Open installment → 200; GET payable → isHeld == true + holdReason set ────────
    [Fact]
    public async Task Hold_OpenInstallment_Returns200_And_InstallmentIsHeld()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, "CC-HOLD1", "Hold Test");
        var vendorId = await FinanceSeed.CreateVendorAsync(client, companyId, "Fornecedor Hold");

        var (payableId, installmentId) = await FinanceSeed.CreatePayableWithInstallmentAsync(factory, 
            client, companyId, vendorId, costCenterId, new DateOnly(2026, 8, 1), 500m, approve: true);

        // Act: hold the installment
        var holdRes = await client.PostAsJsonAsync(
            $"/api/payables/{payableId}/installments/{installmentId}/hold",
            new { reason = "Aguardando aprovação jurídica" });

        Assert.Equal(HttpStatusCode.OK, holdRes.StatusCode);

        // Assert: GET payable detail shows isHeld = true with the reason
        var detail = await ReadJson(await client.GetAsync($"/api/payables/{payableId}"));
        var inst = detail.GetProperty("installments").EnumerateArray()
            .First(i => i.GetProperty("id").GetString() == installmentId);

        Assert.True(inst.GetProperty("isHeld").GetBoolean());
        Assert.Equal("Aguardando aprovação jurídica", inst.GetProperty("holdReason").GetString());
    }

    // ── 2. Release → 200; isHeld == false ────────────────────────────────────────────────────────
    [Fact]
    public async Task ReleaseHold_AfterHold_Returns200_And_InstallmentIsNotHeld()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, "CC-HOLD2", "Release Test");
        var vendorId = await FinanceSeed.CreateVendorAsync(client, companyId, "Fornecedor Release");

        var (payableId, installmentId) = await FinanceSeed.CreatePayableWithInstallmentAsync(factory, 
            client, companyId, vendorId, costCenterId, new DateOnly(2026, 8, 1), 500m, approve: true);

        // Hold first
        var holdRes = await client.PostAsJsonAsync(
            $"/api/payables/{payableId}/installments/{installmentId}/hold",
            new { reason = "Verificar nota" });
        Assert.Equal(HttpStatusCode.OK, holdRes.StatusCode);

        // Act: release
        var releaseRes = await client.PostAsJsonAsync(
            $"/api/payables/{payableId}/installments/{installmentId}/release-hold",
            new { });

        Assert.Equal(HttpStatusCode.OK, releaseRes.StatusCode);

        // Assert: isHeld = false
        var detail = await ReadJson(await client.GetAsync($"/api/payables/{payableId}"));
        var inst = detail.GetProperty("installments").EnumerateArray()
            .First(i => i.GetProperty("id").GetString() == installmentId);

        Assert.False(inst.GetProperty("isHeld").GetBoolean());
    }

    // ── 3. Hold an installment that is InRemittance → 409 ────────────────────────────────────────
    [Fact]
    public async Task Hold_InRemittanceInstallment_Returns409()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, "CC-HOLD3", "Remittance Test");
        var vendorId = await FinanceSeed.CreateVendorAsync(client, companyId, "Fornecedor Remessa");

        var (payableId, installmentId) = await FinanceSeed.CreatePayableWithInstallmentAsync(factory, 
            client, companyId, vendorId, costCenterId, new DateOnly(2026, 8, 1), 500m, approve: true);

        // Parse raw GUIDs from the prefixed ids (wire form: "<prefix>_<guidN>")
        var payableGuid = Guid.ParseExact(payableId.Split('_')[1], "N");
        var installmentGuid = Guid.ParseExact(installmentId.Split('_')[1], "N");

        // Force the installment into InRemittance directly via the domain — no remittance endpoint needed.
        await using var scope = factory.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var payable = await uow.Set<Payable>()
            .Include(p => p.Installments)
            .SingleAsync(p => p.Id == payableGuid);

        var installment = payable.FindInstallment(installmentGuid);
        Assert.NotNull(installment);

        installment.MarkInRemittance();
        await uow.SaveChangesAsync(CancellationToken.None);

        // Act: try to hold the InRemittance installment — must return 409 Conflict.
        var holdRes = await client.PostAsJsonAsync(
            $"/api/payables/{payableId}/installments/{installmentId}/hold",
            new { reason = "Test" });

        Assert.Equal(HttpStatusCode.Conflict, holdRes.StatusCode);
    }

    // ── 4. Hold a non-existent installment → 404 ─────────────────────────────────────────────────
    [Fact]
    public async Task Hold_NonExistentInstallment_Returns404()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, "CC-HOLD4", "NotFound Test");
        var vendorId = await FinanceSeed.CreateVendorAsync(client, companyId, "Fornecedor 404");

        var (payableId, _) = await FinanceSeed.CreatePayableWithInstallmentAsync(factory, 
            client, companyId, vendorId, costCenterId, new DateOnly(2026, 8, 1), 500m, approve: true);

        var fakeInstallmentId = $"installment_{Guid.NewGuid():N}";

        // Act
        var holdRes = await client.PostAsJsonAsync(
            $"/api/payables/{payableId}/installments/{fakeInstallmentId}/hold",
            new { reason = "Test" });

        Assert.Equal(HttpStatusCode.NotFound, holdRes.StatusCode);
    }
}
