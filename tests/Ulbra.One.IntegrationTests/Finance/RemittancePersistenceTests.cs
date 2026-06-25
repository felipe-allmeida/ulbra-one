using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Finance.Domain.Remittances;

namespace Ulbra.One.IntegrationTests.Finance;

/// <summary>
/// Round-trip persistence proof for the Remittance aggregate.
/// Verifies that EF configs + migration produce the correct schema:
/// two lines auto-included, Reference round-tripped, Status stored as string,
/// FileStorageKey stored, and audit CreatedAt stamped by the AuditInterceptor.
/// </summary>
[Collection("Postgres")]
public class RemittancePersistenceTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Remittance_With_Two_Lines_Persists_And_Reloads_Correctly()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        // Ensure the schema is migrated before we write.
        _ = await factory.CreateAdminClientAsync();

        var companyId = Guid.NewGuid();
        var payingBankAccountId = Guid.NewGuid();
        var payableId = Guid.NewGuid();
        var installmentIdA = Guid.NewGuid();
        var installmentIdB = Guid.NewGuid();
        const string bankCode = "341";
        const int sequenceNumber = 42;
        const string generatedBy = "user-test";

        var remittance = Remittance.Create(
            companyId,
            payingBankAccountId,
            bankCode,
            sequenceNumber,
            generatedBy,
            DateTimeOffset.UtcNow);

        var draftA = new RemittanceLineDraft(
            PayableId: payableId,
            InstallmentId: installmentIdA,
            Method: PaymentMethod.Pix,
            Amount: 1500.00m,
            FavoredName: "Fornecedor Alpha Ltda",
            FavoredDocument: "12345678000195",
            FavoredBankCode: "341",
            FavoredAgency: "0001",
            FavoredAccount: "123456",
            FavoredAccountDigit: "7",
            FavoredPixKey: "alpha@pix.com");

        var draftB = new RemittanceLineDraft(
            PayableId: payableId,
            InstallmentId: installmentIdB,
            Method: PaymentMethod.Ted,
            Amount: 800.50m,
            FavoredName: "Fornecedor Beta SA",
            FavoredDocument: "98765432000100",
            FavoredBankCode: "033",
            FavoredAgency: "0002",
            FavoredAccount: "654321",
            FavoredAccountDigit: "9",
            FavoredPixKey: "");

        remittance.AddLine(draftA);
        remittance.AddLine(draftB);
        remittance.SetFile("storage/remittances/rem-42.cnab", "rem-42.cnab", 4096L);

        Guid savedId;
        using (var scope = factory.Services.CreateScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.AddAsync(remittance, CancellationToken.None);
            await uow.SaveChangesAsync(CancellationToken.None);
            savedId = remittance.Id;
        }

        // New scope — clear tracker, reload from DB.
        using (var scope = factory.Services.CreateScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            // AutoInclude is configured on Lines, so no explicit Include needed.
            var loaded = await uow.ReadSet<Remittance>()
                .FirstAsync(r => r.Id == savedId);

            // Lines auto-included and count correct.
            Assert.Equal(2, loaded.Lines.Count);

            // Reference is computed as $"{SequenceNumber:D6}{Sequence:D5}" — e.g. "00004200001".
            var expectedRefA = $"{sequenceNumber:D6}{1:D5}";
            var expectedRefB = $"{sequenceNumber:D6}{2:D5}";
            Assert.Contains(loaded.Lines, l => l.Reference == expectedRefA);
            Assert.Contains(loaded.Lines, l => l.Reference == expectedRefB);

            // FileStorageKey stored.
            Assert.Equal("storage/remittances/rem-42.cnab", loaded.FileStorageKey);
            Assert.Equal("rem-42.cnab", loaded.FileName);
            Assert.Equal(4096L, loaded.FileSizeBytes);

            // Status stored as string (enum name) and correct value.
            Assert.Equal(RemittanceStatus.Generated, loaded.Status);

            // BankCode round-tripped.
            Assert.Equal(bankCode, loaded.BankCode);
            Assert.Equal(sequenceNumber, loaded.SequenceNumber);

            // Audit CreatedAt stamped by the AuditInterceptor (non-default).
            Assert.NotEqual(default, loaded.CreatedAt);

            // Methods stored as strings.
            var lineA = loaded.Lines.First(l => l.Reference == expectedRefA);
            Assert.Equal(PaymentMethod.Pix, lineA.Method);
            Assert.Equal(RemittanceLineStatus.Pending, lineA.Status);
            Assert.Equal(1500.00m, lineA.Amount);

            var lineB = loaded.Lines.First(l => l.Reference == expectedRefB);
            Assert.Equal(PaymentMethod.Ted, lineB.Method);
            Assert.Equal(800.50m, lineB.Amount);
        }
    }
}
