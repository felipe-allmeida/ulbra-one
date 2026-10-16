using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Finance.Domain.Remittances;

namespace Ulbra.One.IntegrationTests.Finance;

/// <summary>
/// Round-trip persistence proof for the RemittanceReturn entity and the new RemittanceLine return columns.
/// Verifies that EF configs + migration produce the correct schema:
/// RemittanceReturn saves and reloads all fields (FileStorageKey, FileName, ProcessedBy, counts, ProcessedAt, audit);
/// RemittanceLine.MarkPaid persists BankReference + EffectiveDate; MarkReturned persists ReturnReason.
/// </summary>
[Collection("Postgres")]
public class RemittanceReturnPersistenceTests(PostgresFixture fixture)
{
    [Fact]
    public async Task RemittanceReturn_Persists_And_Reloads_All_Fields()
    {
        var factory = fixture.Api;
        // Ensure the schema is migrated before we write.
        _ = await factory.CreateAdminClientAsync();

        var companyId = Guid.NewGuid();
        var payingBankAccountId = Guid.NewGuid();
        const string fileStorageKey = "storage/returns/ret-001.cnab";
        const string fileName = "ret-001.cnab";
        var processedAt = DateTimeOffset.UtcNow;
        const string processedBy = "user-test";

        var remittanceReturn = RemittanceReturn.Create(
            companyId: companyId,
            payingBankAccountId: payingBankAccountId,
            fileStorageKey: fileStorageKey,
            fileName: fileName,
            matchedCount: 10,
            paidCount: 8,
            returnedCount: 1,
            unmatchedCount: 1,
            divergentCount: 0,
            processedBy: processedBy,
            processedAt: processedAt,
            applied: false);

        Guid savedId;
        using (var scope = factory.Services.CreateScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.AddAsync(remittanceReturn, CancellationToken.None);
            await uow.SaveChangesAsync(CancellationToken.None);
            savedId = remittanceReturn.Id;
        }

        // New scope — clear tracker, reload from DB.
        using (var scope = factory.Services.CreateScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var loaded = await uow.ReadSet<RemittanceReturn>()
                .FirstAsync(r => r.Id == savedId);

            Assert.Equal(companyId, loaded.CompanyId);
            Assert.Equal(payingBankAccountId, loaded.PayingBankAccountId);
            Assert.False(loaded.Applied);
            Assert.Equal(fileStorageKey, loaded.FileStorageKey);
            Assert.Equal(fileName, loaded.FileName);
            Assert.Equal(10, loaded.MatchedCount);
            Assert.Equal(8, loaded.PaidCount);
            Assert.Equal(1, loaded.ReturnedCount);
            Assert.Equal(1, loaded.UnmatchedCount);
            Assert.Equal(0, loaded.DivergentCount);
            Assert.Equal(processedBy, loaded.ProcessedBy);
            Assert.Equal(processedAt.ToUnixTimeSeconds(), loaded.ProcessedAt.ToUnixTimeSeconds());
            Assert.Null(loaded.DeletedAt);
            // Audit CreatedAt stamped by the AuditInterceptor (non-default).
            Assert.NotEqual(default, loaded.CreatedAt);
        }
    }

    [Fact]
    public async Task RemittanceLine_MarkPaid_Persists_BankReference_And_EffectiveDate()
    {
        var factory = fixture.Api;
        _ = await factory.CreateAdminClientAsync();

        var companyId = Guid.NewGuid();
        var payingBankAccountId = Guid.NewGuid();
        var payableId = Guid.NewGuid();
        var installmentId = Guid.NewGuid();

        var remittance = Remittance.Create(
            companyId,
            payingBankAccountId,
            bankCode: "341",
            sequenceNumber: 77,
            generatedBy: "user-test",
            DateTimeOffset.UtcNow);

        var draft = new RemittanceLineDraft(
            PayableId: payableId,
            InstallmentId: installmentId,
            Method: PaymentMethod.Pix,
            Amount: 500.00m,
            FavoredName: "Fornecedor Teste Ltda",
            FavoredDocument: "12345678000195",
            FavoredBankCode: "341",
            FavoredAgency: "0001",
            FavoredAccount: "111111",
            FavoredAccountDigit: "1",
            FavoredPixKey: "teste@pix.com");

        remittance.AddLine(draft);
        var line = remittance.Lines.Single();

        var effectiveDate = new DateOnly(2026, 6, 25);
        const string bankReference = "BANKREF001";
        line.MarkPaid(effectiveDate, bankReference);

        Guid lineId;
        using (var scope = factory.Services.CreateScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.AddAsync(remittance, CancellationToken.None);
            await uow.SaveChangesAsync(CancellationToken.None);
            lineId = line.Id;
        }

        using (var scope = factory.Services.CreateScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var loaded = await uow.ReadSet<RemittanceLine>()
                .FirstAsync(l => l.Id == lineId);

            Assert.Equal(RemittanceLineStatus.Paid, loaded.Status);
            Assert.Equal(effectiveDate, loaded.EffectiveDate);
            Assert.Equal(bankReference, loaded.BankReference);
            Assert.Null(loaded.ReturnReason);
        }
    }

    [Fact]
    public async Task RemittanceLine_MarkReturned_Persists_ReturnReason()
    {
        var factory = fixture.Api;
        _ = await factory.CreateAdminClientAsync();

        var companyId = Guid.NewGuid();
        var payingBankAccountId = Guid.NewGuid();
        var payableId = Guid.NewGuid();
        var installmentId = Guid.NewGuid();

        var remittance = Remittance.Create(
            companyId,
            payingBankAccountId,
            bankCode: "033",
            sequenceNumber: 88,
            generatedBy: "user-test",
            DateTimeOffset.UtcNow);

        var draft = new RemittanceLineDraft(
            PayableId: payableId,
            InstallmentId: installmentId,
            Method: PaymentMethod.Ted,
            Amount: 200.00m,
            FavoredName: "Fornecedor Rejeitado Ltda",
            FavoredDocument: "98765432000100",
            FavoredBankCode: "033",
            FavoredAgency: "0002",
            FavoredAccount: "222222",
            FavoredAccountDigit: "2",
            FavoredPixKey: "");

        remittance.AddLine(draft);
        var line = remittance.Lines.Single();
        const string returnReason = "AC10 - Agência/conta inválida";
        line.MarkReturned(returnReason);

        Guid lineId;
        using (var scope = factory.Services.CreateScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.AddAsync(remittance, CancellationToken.None);
            await uow.SaveChangesAsync(CancellationToken.None);
            lineId = line.Id;
        }

        using (var scope = factory.Services.CreateScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var loaded = await uow.ReadSet<RemittanceLine>()
                .FirstAsync(l => l.Id == lineId);

            Assert.Equal(RemittanceLineStatus.Returned, loaded.Status);
            Assert.Equal(returnReason, loaded.ReturnReason);
            Assert.Null(loaded.EffectiveDate);
            Assert.Null(loaded.BankReference);
        }
    }
}
