using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Finance.Domain.Remittances;
using Xunit;

namespace Ulbra.One.UnitTests.Finance;

public class RemittanceReturnDomainTests
{
    private static readonly DateTimeOffset At = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid BankAccountId = Guid.NewGuid();

    private static Remittance NewRemittance() =>
        Remittance.Create(CompanyId, BankAccountId, "341", 1, "system", At);

    private static RemittanceLineDraft NewDraft(decimal amount = 500m) =>
        new(
            PayableId: Guid.NewGuid(),
            InstallmentId: Guid.NewGuid(),
            Method: PaymentMethod.Pix,
            Amount: amount,
            FavoredName: "Fornecedor",
            FavoredDocument: "12345678000100",
            FavoredBankCode: "341",
            FavoredAgency: "0001",
            FavoredAccount: "123456",
            FavoredAccountDigit: "7",
            FavoredPixKey: "chave@pix.com"
        );

    // ── MarkPaid ────────────────────────────────────────────────────────────

    [Fact]
    public void MarkPaid_transitions_Pending_line_to_Paid_and_sets_fields()
    {
        var r = NewRemittance();
        var line = r.AddLine(NewDraft());
        var effectiveDate = new DateOnly(2026, 7, 2);

        line.MarkPaid(effectiveDate, "BANK-REF-001");

        Assert.Equal(RemittanceLineStatus.Paid, line.Status);
        Assert.Equal(effectiveDate, line.EffectiveDate);
        Assert.Equal("BANK-REF-001", line.BankReference);
    }

    [Fact]
    public void MarkPaid_null_bankReference_is_allowed()
    {
        var r = NewRemittance();
        var line = r.AddLine(NewDraft());

        line.MarkPaid(new DateOnly(2026, 7, 2), null);

        Assert.Equal(RemittanceLineStatus.Paid, line.Status);
        Assert.Null(line.BankReference);
    }

    [Fact]
    public void MarkPaid_on_non_Pending_line_throws_InvalidOperationException()
    {
        var r = NewRemittance();
        var line = r.AddLine(NewDraft());
        line.MarkPaid(new DateOnly(2026, 7, 2), "REF");

        Assert.Throws<InvalidOperationException>(() => line.MarkPaid(new DateOnly(2026, 7, 3), "REF2"));
    }

    // ── MarkReturned ────────────────────────────────────────────────────────

    [Fact]
    public void MarkReturned_transitions_Pending_line_to_Returned_and_sets_reason()
    {
        var r = NewRemittance();
        var line = r.AddLine(NewDraft());

        line.MarkReturned("AC03 - Invalid account");

        Assert.Equal(RemittanceLineStatus.Returned, line.Status);
        Assert.Equal("AC03 - Invalid account", line.ReturnReason);
    }

    [Fact]
    public void MarkReturned_on_non_Pending_line_throws_InvalidOperationException()
    {
        var r = NewRemittance();
        var line = r.AddLine(NewDraft());
        line.MarkReturned("reason");

        Assert.Throws<InvalidOperationException>(() => line.MarkReturned("reason2"));
    }

    // ── Recompute ────────────────────────────────────────────────────────────

    [Fact]
    public void Recompute_all_Paid_no_Returned_transitions_to_Confirmed()
    {
        var r = NewRemittance();
        var line1 = r.AddLine(NewDraft());
        var line2 = r.AddLine(NewDraft());
        line1.MarkPaid(new DateOnly(2026, 7, 2), null);
        line2.MarkPaid(new DateOnly(2026, 7, 2), null);

        r.Recompute();

        Assert.Equal(RemittanceStatus.Confirmed, r.Status);
    }

    [Fact]
    public void Recompute_some_Returned_rest_Paid_no_Pending_transitions_to_PartiallyReturned()
    {
        var r = NewRemittance();
        var line1 = r.AddLine(NewDraft());
        var line2 = r.AddLine(NewDraft());
        line1.MarkPaid(new DateOnly(2026, 7, 2), null);
        line2.MarkReturned("AC03");

        r.Recompute();

        Assert.Equal(RemittanceStatus.PartiallyReturned, r.Status);
    }

    [Fact]
    public void Recompute_all_Returned_no_Pending_transitions_to_PartiallyReturned()
    {
        var r = NewRemittance();
        var line1 = r.AddLine(NewDraft());
        var line2 = r.AddLine(NewDraft());
        line1.MarkReturned("reason");
        line2.MarkReturned("reason");

        r.Recompute();

        Assert.Equal(RemittanceStatus.PartiallyReturned, r.Status);
    }

    [Fact]
    public void Recompute_any_Pending_line_stays_Generated()
    {
        var r = NewRemittance();
        var line1 = r.AddLine(NewDraft());
        var line2 = r.AddLine(NewDraft());
        line1.MarkPaid(new DateOnly(2026, 7, 2), null);
        // line2 stays Pending

        r.Recompute();

        Assert.Equal(RemittanceStatus.Generated, r.Status);
    }

    [Fact]
    public void Recompute_does_not_change_Cancelled_remittance()
    {
        var r = NewRemittance();
        r.AddLine(NewDraft());
        r.Cancel();

        r.Recompute();

        Assert.Equal(RemittanceStatus.Cancelled, r.Status);
    }

    // ── RemittanceReturn factory ──────────────────────────────────────────

    [Fact]
    public void RemittanceReturn_Create_sets_all_properties()
    {
        var companyId = Guid.NewGuid();
        var payingBankAccountId = Guid.NewGuid();
        var processedAt = new DateTimeOffset(2026, 7, 3, 10, 0, 0, TimeSpan.Zero);

        var ret = RemittanceReturn.Create(
            companyId: companyId,
            payingBankAccountId: payingBankAccountId,
            fileStorageKey: "returns/ret001.ret",
            fileName: "ret001.ret",
            matchedCount: 5,
            paidCount: 4,
            returnedCount: 1,
            unmatchedCount: 0,
            divergentCount: 0,
            processedBy: "user-import",
            processedAt: processedAt
        );

        Assert.NotEqual(Guid.Empty, ret.Id);
        Assert.Equal(companyId, ret.CompanyId);
        Assert.Equal(payingBankAccountId, ret.PayingBankAccountId);
        // Applied defaults to true: the factory's callers only record a file that was actually processed,
        // and the divergent-trailer path passes it explicitly.
        Assert.True(ret.Applied);
        Assert.Equal("returns/ret001.ret", ret.FileStorageKey);
        Assert.Equal("ret001.ret", ret.FileName);
        Assert.Equal(5, ret.MatchedCount);
        Assert.Equal(4, ret.PaidCount);
        Assert.Equal(1, ret.ReturnedCount);
        Assert.Equal(0, ret.UnmatchedCount);
        Assert.Equal(0, ret.DivergentCount);
        Assert.Equal("user-import", ret.ProcessedBy);
        Assert.Equal(processedAt, ret.ProcessedAt);
    }

    [Fact]
    public void RemittanceReturn_MarkCreated_sets_audit_fields()
    {
        var ret = RemittanceReturn.Create(Guid.NewGuid(), Guid.NewGuid(), "k", "f", 1, 1, 0, 0, 0, "u", At);
        ret.MarkCreated("user1", At);

        Assert.Equal("user1", ret.CreatedBy);
        Assert.Equal(At, ret.CreatedAt);
        Assert.Equal("user1", ret.UpdatedBy);
        Assert.Equal(At, ret.UpdatedAt);
    }

    [Fact]
    public void RemittanceReturn_MarkDeleted_sets_DeletedAt()
    {
        var ret = RemittanceReturn.Create(Guid.NewGuid(), Guid.NewGuid(), "k", "f", 1, 1, 0, 0, 0, "u", At);
        ret.MarkDeleted(At);

        Assert.Equal(At, ret.DeletedAt);
    }
}
