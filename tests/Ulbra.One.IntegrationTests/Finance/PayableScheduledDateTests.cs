using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using System.Text.Json;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.IntegrationTests.Finance;

/// <summary>
/// Verifies that the CreatePayable handler resolves installment scheduled payment dates
/// from the vendor's PaymentSchedule policy at creation time.
/// </summary>
[Collection("Postgres")]
public class PayableScheduledDateTests(PostgresFixture fixture)
{
    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task CreatePayable_WithVendorFixedDaySchedule_SetsScheduledPaymentDateToDay15()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        // Arrange: company + cost center
        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, "CC-SCHED", "Scheduling");

        // Arrange: vendor with FixedDayOfMonth = 15
        var vendorRes = await client.PostAsJsonAsync("/api/vendors", new
        {
            companyId,
            personType = 1,
            name = "Fornecedor Schedule Ltda",
            tradeName = "Fornecedor Schedule",
            document = FinanceSeed.RandomCnpj(),
            email = "sched@test.com",
            phone = "5133334444",
            address = (object?)null,
            paymentTerms = "30 dias",
            bankInfo = (string?)null,
            taxRegime = 0,
            paymentScheduleMode = 1, // FixedDayOfMonth
            paymentScheduleDay = 15,
        });
        vendorRes.EnsureSuccessStatusCode();
        var vendorId = (await ReadJson(vendorRes)).GetProperty("id").GetString()!;

        // Act: create payable with single installment due 2026-07-05
        var createRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId,
            vendorId,
            costCenterId,
            description = "Teste scheduled date",
            installments = new[] { new { dueDate = "2026-07-05", amount = 1000m } },
        });
        createRes.EnsureSuccessStatusCode();
        var body = await ReadJson(createRes);
        var payableIdStr = body.GetProperty("id").GetString()!;

        // Parse the payable Id (strip the prefix "payable_")
        var payableId = Guid.Parse(payableIdStr["payable_".Length..]);

        // Assert: load installment directly from DB and verify scheduledPaymentDate == 2026-07-15
        using var scope = factory.Services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var inst = await uow.ReadSet<Installment>()
            .SingleAsync(i => i.PayableId == payableId);

        Assert.Equal(new DateOnly(2026, 7, 15), inst.ScheduledPaymentDate);
    }

    [Fact]
    public async Task CreatePayable_WithVendorOnDueDateSchedule_SetsScheduledPaymentDateEqualDueDate()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        // Arrange: company + cost center + vendor with default OnDueDate schedule
        var companyId = await FinanceSeed.CreateCompanyAsync(client);
        var costCenterId = await FinanceSeed.CreateCostCenterAsync(client, companyId, "CC-SCHED2", "Scheduling2");
        var vendorId = await FinanceSeed.CreateVendorAsync(client, companyId, "Fornecedor OnDueDate Ltda");

        // Act: create payable with single installment due 2026-07-05
        var createRes = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId,
            vendorId,
            costCenterId,
            description = "Teste scheduled date on due date",
            installments = new[] { new { dueDate = "2026-07-05", amount = 500m } },
        });
        createRes.EnsureSuccessStatusCode();
        var body = await ReadJson(createRes);
        var payableIdStr = body.GetProperty("id").GetString()!;
        var payableId = Guid.Parse(payableIdStr["payable_".Length..]);

        // Assert: scheduled date equals due date
        using var scope = factory.Services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var inst = await uow.ReadSet<Installment>()
            .SingleAsync(i => i.PayableId == payableId);

        Assert.Equal(new DateOnly(2026, 7, 5), inst.ScheduledPaymentDate);
    }
}
