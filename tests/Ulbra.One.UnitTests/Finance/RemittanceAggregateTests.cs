using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Finance.Domain.Remittances;
using Xunit;

namespace Ulbra.One.UnitTests.Finance;

public class RemittanceAggregateTests
{
    private static readonly DateTimeOffset At = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid BankAccountId = Guid.NewGuid();

    private static Remittance NewRemittance(int sequenceNumber = 42) =>
        Remittance.Create(CompanyId, BankAccountId, "341", sequenceNumber, "system", At);

    private static RemittanceLineDraft NewDraft(decimal amount = 1000m) =>
        new(
            PayableId: Guid.NewGuid(),
            InstallmentId: Guid.NewGuid(),
            Method: PaymentMethod.Pix,
            Amount: amount,
            FavoredName: "Fornecedor ABC",
            FavoredDocument: "12345678000100",
            FavoredBankCode: "341",
            FavoredAgency: "0001",
            FavoredAccount: "123456",
            FavoredAccountDigit: "7",
            FavoredPixKey: "chave@pix.com"
        );

    [Fact]
    public void Create_sets_Generated_status_and_empty_lines()
    {
        var r = NewRemittance();

        Assert.Equal(RemittanceStatus.Generated, r.Status);
        Assert.Empty(r.Lines);
        Assert.Equal(CompanyId, r.CompanyId);
        Assert.Equal(BankAccountId, r.PayingBankAccountId);
        Assert.Equal("341", r.BankCode);
        Assert.Equal(42, r.SequenceNumber);
        Assert.Equal("system", r.GeneratedBy);
        Assert.Equal(At, r.GeneratedAt);
        Assert.Null(r.FileStorageKey);
        Assert.Null(r.FileName);
        Assert.Null(r.FileSizeBytes);
    }

    [Fact]
    public void AddLine_appends_line_with_sequence_one_based()
    {
        var r = NewRemittance(sequenceNumber: 1);

        var line1 = r.AddLine(NewDraft(500m));
        var line2 = r.AddLine(NewDraft(300m));

        Assert.Equal(2, r.Lines.Count);
        Assert.Equal(1, line1.Sequence);
        Assert.Equal(2, line2.Sequence);
    }

    [Fact]
    public void AddLine_computes_Reference_from_sequence_number_and_sequence()
    {
        var r = NewRemittance(sequenceNumber: 42);

        var line1 = r.AddLine(NewDraft());
        var line2 = r.AddLine(NewDraft());

        // Reference = $"{SequenceNumber:D6}{Sequence:D5}" → "000042" + "00001" = "00004200001"
        Assert.Equal("00004200001", line1.Reference);
        Assert.Equal("00004200002", line2.Reference);
    }

    [Fact]
    public void AddLine_sets_RemittanceId_on_new_line()
    {
        var r = NewRemittance();
        var line = r.AddLine(NewDraft());

        Assert.Equal(r.Id, line.RemittanceId);
    }

    [Fact]
    public void AddLine_line_status_is_Pending()
    {
        var r = NewRemittance();
        var line = r.AddLine(NewDraft());

        Assert.Equal(RemittanceLineStatus.Pending, line.Status);
    }

    [Fact]
    public void TotalAmount_sums_all_line_amounts()
    {
        var r = NewRemittance();
        r.AddLine(NewDraft(500m));
        r.AddLine(NewDraft(300m));
        r.AddLine(NewDraft(200m));

        Assert.Equal(1000m, r.TotalAmount);
    }

    [Fact]
    public void TotalAmount_is_zero_when_no_lines()
    {
        var r = NewRemittance();
        Assert.Equal(0m, r.TotalAmount);
    }

    [Fact]
    public void Cancel_from_Generated_transitions_to_Cancelled()
    {
        var r = NewRemittance();
        r.Cancel();

        Assert.Equal(RemittanceStatus.Cancelled, r.Status);
    }

    [Fact]
    public void Cancel_twice_throws_InvalidOperationException()
    {
        var r = NewRemittance();
        r.Cancel();

        Assert.Throws<InvalidOperationException>(() => r.Cancel());
    }

    [Fact]
    public void SetFile_stores_key_name_and_size()
    {
        var r = NewRemittance();
        r.SetFile("remittances/file.rem", "file.rem", 4096L);

        Assert.Equal("remittances/file.rem", r.FileStorageKey);
        Assert.Equal("file.rem", r.FileName);
        Assert.Equal(4096L, r.FileSizeBytes);
    }

    [Fact]
    public void Create_sets_audit_fields()
    {
        var r = NewRemittance();
        r.MarkCreated("user1", At);

        Assert.Equal("user1", r.CreatedBy);
        Assert.Equal(At, r.CreatedAt);
        Assert.Equal("user1", r.UpdatedBy);
        Assert.Equal(At, r.UpdatedAt);
    }

    [Fact]
    public void MarkUpdated_updates_audit_fields()
    {
        var r = NewRemittance();
        r.MarkCreated("user1", At);
        var later = At.AddHours(1);
        r.MarkUpdated("user2", later);

        Assert.Equal("user2", r.UpdatedBy);
        Assert.Equal(later, r.UpdatedAt);
        // CreatedBy/CreatedAt unchanged
        Assert.Equal("user1", r.CreatedBy);
        Assert.Equal(At, r.CreatedAt);
    }

    [Fact]
    public void MarkDeleted_sets_DeletedAt()
    {
        var r = NewRemittance();
        r.MarkDeleted(At);

        Assert.Equal(At, r.DeletedAt);
    }

    [Fact]
    public void AddLine_captures_draft_favored_fields()
    {
        var r = NewRemittance();
        var draft = new RemittanceLineDraft(
            PayableId: Guid.NewGuid(),
            InstallmentId: Guid.NewGuid(),
            Method: PaymentMethod.Ted,
            Amount: 750m,
            FavoredName: "Empresa XYZ",
            FavoredDocument: "98765432000111",
            FavoredBankCode: "001",
            FavoredAgency: "1234",
            FavoredAccount: "654321",
            FavoredAccountDigit: "0",
            FavoredPixKey: ""
        );

        var line = r.AddLine(draft);

        Assert.Equal(draft.PayableId, line.PayableId);
        Assert.Equal(draft.InstallmentId, line.InstallmentId);
        Assert.Equal(PaymentMethod.Ted, line.Method);
        Assert.Equal(750m, line.Amount);
        Assert.Equal("Empresa XYZ", line.FavoredName);
        Assert.Equal("98765432000111", line.FavoredDocument);
        Assert.Equal("001", line.FavoredBankCode);
        Assert.Equal("1234", line.FavoredAgency);
        Assert.Equal("654321", line.FavoredAccount);
        Assert.Equal("0", line.FavoredAccountDigit);
        Assert.Equal("", line.FavoredPixKey);
    }
}
