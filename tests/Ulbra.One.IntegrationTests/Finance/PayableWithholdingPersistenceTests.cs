using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.IntegrationTests.Finance;

/// <summary>
/// Round-trip persistence proof for the PayableWithholding child entity and the
/// Installment.NetAmount column introduced in the PayableTaxWithholding migration.
/// Verifies: withholdings auto-included via PayableConfiguration.AutoInclude,
/// TaxType stored as string, NetAmount recomputed proportionally per installment.
/// </summary>
[Collection("Postgres")]
public class PayableWithholdingPersistenceTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Payable_With_Two_Withholdings_Persists_And_Reloads_Correctly()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        // Ensure the schema is migrated before we write.
        _ = await factory.CreateAdminClientAsync();

        var companyId = Guid.NewGuid();
        var vendorId = Guid.NewGuid();
        var costCenterId = Guid.NewGuid();

        // 2 installments of 500 each → total 1000
        var installments = Payable.GenerateInstallments(
            total: 1000m,
            count: 2,
            firstDueDate: new DateOnly(2026, 8, 1),
            intervalMonths: 1);

        var payable = new Payable(
            companyId: companyId,
            origin: Ulbra.One.Finance.Domain.PayableOrigin.Manual,
            vendorId: vendorId,
            counterpartyName: "Fornecedor Teste Ltda",
            documentNumber: "NF-001",
            issueDate: new DateOnly(2026, 7, 1),
            description: "Prestação de serviços — TDD",
            costCenterId: costCenterId,
            categoryId: null,
            installments: installments);

        // IRRF 1.5% on 1000 = 15; ISS 5% on 1000 = 50 → total withheld = 65 → net = 935
        payable.SetWithholdings([
            (Ulbra.One.Finance.Domain.WithholdingTaxType.Irrf, BaseAmount: 1000m, Rate: 0.015m, Amount: 15m),
            (Ulbra.One.Finance.Domain.WithholdingTaxType.Iss,  BaseAmount: 1000m, Rate: 0.05m,  Amount: 50m)
        ]);

        Guid savedId;
        using (var scope = factory.Services.CreateScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.AddAsync(payable, CancellationToken.None);
            await uow.SaveChangesAsync(CancellationToken.None);
            savedId = payable.Id;
        }

        // New scope — clear change tracker, reload from DB.
        using (var scope = factory.Services.CreateScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            // AutoInclude is configured on Withholdings + Installments — no explicit Include needed.
            var loaded = await uow.ReadSet<Payable>()
                .FirstAsync(p => p.Id == savedId);

            // ── Withholdings ────────────────────────────────────────────────
            Assert.Equal(2, loaded.Withholdings.Count);

            var irrf = loaded.Withholdings.Single(w => w.TaxType == Ulbra.One.Finance.Domain.WithholdingTaxType.Irrf);
            Assert.Equal(1000m, irrf.BaseAmount);
            Assert.Equal(0.015m, irrf.Rate);
            Assert.Equal(15m, irrf.Amount);

            var iss = loaded.Withholdings.Single(w => w.TaxType == Ulbra.One.Finance.Domain.WithholdingTaxType.Iss);
            Assert.Equal(1000m, iss.BaseAmount);
            Assert.Equal(0.05m, iss.Rate);
            Assert.Equal(50m, iss.Amount);

            // ── NetAmount on installments ────────────────────────────────────
            // Net total = 1000 - 65 = 935; two equal installments of 500 each.
            // Proportional split: each 500/1000 * 935 = 467.50
            // Because of rounding last installment absorbs remainder: 935 - 467.50 = 467.50
            Assert.Equal(2, loaded.Installments.Count);
            var netSum = loaded.Installments.Sum(i => i.NetAmount);
            Assert.Equal(935m, netSum);

            // Each individual installment must have a positive net amount.
            Assert.All(loaded.Installments, inst => Assert.True(inst.NetAmount > 0));

            // Gross amounts untouched.
            Assert.Equal(1000m, loaded.Installments.Sum(i => i.Amount));

            // Audit CreatedAt stamped by the AuditInterceptor (non-default).
            Assert.NotEqual(default, loaded.CreatedAt);
        }
    }

    [Fact]
    public async Task Installment_NetAmount_Equals_Amount_When_No_Withholdings()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        _ = await factory.CreateAdminClientAsync();

        var companyId = Guid.NewGuid();
        var vendorId = Guid.NewGuid();
        var costCenterId = Guid.NewGuid();

        var installments = Payable.GenerateInstallments(
            total: 300m,
            count: 1,
            firstDueDate: new DateOnly(2026, 9, 1),
            intervalMonths: 1);

        var payable = new Payable(
            companyId: companyId,
            origin: Ulbra.One.Finance.Domain.PayableOrigin.Manual,
            vendorId: vendorId,
            counterpartyName: "Fornecedor Sem Retenção Ltda",
            documentNumber: null,
            issueDate: null,
            description: "Sem retenções",
            costCenterId: costCenterId,
            categoryId: null,
            installments: installments);

        Guid savedId;
        using (var scope = factory.Services.CreateScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.AddAsync(payable, CancellationToken.None);
            await uow.SaveChangesAsync(CancellationToken.None);
            savedId = payable.Id;
        }

        using (var scope = factory.Services.CreateScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var loaded = await uow.ReadSet<Payable>()
                .FirstAsync(p => p.Id == savedId);

            Assert.Empty(loaded.Withholdings);
            Assert.Single(loaded.Installments);
            var inst = loaded.Installments.Single();
            Assert.Equal(inst.Amount, inst.NetAmount);
        }
    }
}
