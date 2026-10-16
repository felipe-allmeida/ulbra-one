using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Xunit;

namespace Ulbra.One.UnitTests.Finance;

public class PayableTests
{
    private static readonly DateTimeOffset At = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    private static Payable NewPayable(decimal total, int count = 1) =>
        new(Guid.NewGuid(), PayableOrigin.Manual, Guid.NewGuid(), "CEEE",
            documentNumber: "NF-1", issueDate: new DateOnly(2026, 7, 1), description: "Energia",
            costCenterId: Guid.NewGuid(), categoryId: null,
            Payable.GenerateInstallments(total, count, new DateOnly(2026, 7, 10), 1));

    [Fact]
    public void New_payable_totals_installments_and_starts_received()
    {
        var p = NewPayable(100m, 4);
        Assert.Equal(100m, p.TotalAmount);
        Assert.Equal(4, p.Installments.Count);
        Assert.Equal(PayableStatus.Received, p.Status);
        Assert.All(p.Installments, i => Assert.Equal(InstallmentStatus.Open, i.Status));
    }

    [Theory]
    [InlineData(PayableOrigin.FiscalDocument)]
    [InlineData(PayableOrigin.Advance)]
    public void New_payable_keeps_intake_origin(PayableOrigin origin)
    {
        var p = new Payable(Guid.NewGuid(), origin, Guid.NewGuid(), "CEEE", null, null, "Energia",
            Guid.NewGuid(), null, Payable.GenerateInstallments(100m, 1, new DateOnly(2026, 7, 10), 1));
        Assert.Equal(origin, p.Origin);
    }

    [Fact]
    public void New_payable_rejects_zero_installments()
    {
        Assert.Throws<ArgumentException>(() =>
            new Payable(Guid.NewGuid(), PayableOrigin.Manual, Guid.NewGuid(), "X", null, null, "d",
                Guid.NewGuid(), null, new List<Installment>()));
    }

    [Fact]
    public void Auto_approval_approves_when_classified_and_within_limit()
    {
        var p = NewPayable(500m);
        p.Classify(Guid.NewGuid());
        p.SetType(PayableType.Service); // type must be determined before auto-approving
        p.ApplyAutoApproval(1000m, At);
        Assert.Equal(PayableStatus.PendingAccounting, p.Status); // approval now routes through accounting
        Assert.Equal("system", p.ApprovedBy);
        Assert.Equal(At, p.ApprovedAt);
    }

    [Fact]
    public void Auto_approval_keeps_received_when_type_undetermined()
    {
        // "Scheduled always withholds": a classified payable whose Type is still null must NOT auto-schedule —
        // withholding is skipped for a non-service, so it would lock in at zero retention.
        var p = NewPayable(500m);
        p.Classify(Guid.NewGuid());
        p.ApplyAutoApproval(1000m, At);
        Assert.Equal(PayableStatus.Received, p.Status);
    }

    [Fact]
    public void Auto_approval_advances_product_with_no_withholding()
    {
        // A determined product (Type=Product) has nothing to withhold, so it may auto-approve
        // (into the accounting stage) — unlike an undetermined type, which stays Received.
        var p = NewPayable(500m);
        p.Classify(Guid.NewGuid());
        p.SetType(PayableType.Product);
        p.ApplyAutoApproval(1000m, At);
        Assert.Equal(PayableStatus.PendingAccounting, p.Status);
    }

    [Fact]
    public void Auto_approval_keeps_received_when_not_classified()
    {
        var p = NewPayable(500m);
        p.ApplyAutoApproval(1000m, At);
        Assert.Equal(PayableStatus.Received, p.Status);
    }

    [Fact]
    public void Auto_approval_keeps_received_when_above_limit()
    {
        var p = NewPayable(1500m);
        p.Classify(Guid.NewGuid());
        p.ApplyAutoApproval(1000m, At);
        Assert.Equal(PayableStatus.Received, p.Status);
    }

    [Fact]
    public void Update_throws_when_payable_has_registered_payments()
    {
        var p = NewPayable(1000m);
        p.Approve("u1", At);
        var inst = p.Installments[0];
        inst.RegisterPayment(new Payment(inst.Id, p.Id, new DateOnly(2026, 7, 10), 1000m, Guid.NewGuid(),
            PaymentMethod.Pix, 0, 0, 0, null, Guid.NewGuid(), default));

        Assert.Throws<InvalidOperationException>(() => p.Update(Guid.NewGuid(), "X", null, null, "edit",
            Guid.NewGuid(), null, Payable.GenerateInstallments(2000m, 1, new DateOnly(2026, 8, 10), 1)));
    }

    [Fact]
    public void Update_allowed_when_a_payment_was_reversed()
    {
        var p = NewPayable(1000m);
        p.Approve("u1", At);
        var inst = p.Installments[0];
        var pay = inst.RegisterPayment(new Payment(inst.Id, p.Id, new DateOnly(2026, 7, 10), 1000m, Guid.NewGuid(),
            PaymentMethod.Pix, 0, 0, 0, null, Guid.NewGuid(), default));
        inst.ReversePayment(pay.Id, "estorno");
        p.RecomputeSettlement();

        // only reversed payments exist → editing is allowed again
        p.Update(Guid.NewGuid(), "X", null, null, "edit", Guid.NewGuid(), null,
            Payable.GenerateInstallments(2000m, 1, new DateOnly(2026, 8, 10), 1));
        Assert.Equal(2000m, p.TotalAmount);
    }

    [Fact]
    public void Reject_sets_reason_and_state()
    {
        var p = NewPayable(100m);
        p.Reject("doc faltando", "u1", At);
        Assert.Equal(PayableStatus.Rejected, p.Status);
        Assert.Equal("doc faltando", p.RejectionReason);
        Assert.Equal("u1", p.RejectedBy);
        Assert.Equal(At, p.RejectedAt);
    }

    [Fact]
    public void Cancel_requires_non_settled_and_records_reason()
    {
        var p = NewPayable(100m);
        p.Cancel("duplicada");
        Assert.Equal(PayableStatus.Cancelled, p.Status);
        Assert.Equal("duplicada", p.CancelReason);
    }

    [Fact]
    public void Cancel_throws_when_already_cancelled()
    {
        var p = NewPayable(100m);
        p.Cancel("x");
        Assert.Throws<InvalidOperationException>(() => p.Cancel("y"));
    }

    [Fact]
    public void Cancel_throws_when_payable_has_registered_payments()
    {
        var p = NewPayable(1000m);
        p.Approve("u1", At);
        p.CompleteAccounting("u1", At, hasFiscalDocument: true); // → Scheduled (payable)
        var inst = p.Installments[0];
        inst.RegisterPayment(new Payment(inst.Id, p.Id, new DateOnly(2026, 7, 10), 400m, Guid.NewGuid(),
            PaymentMethod.Pix, 0, 0, 0, null, Guid.NewGuid(), default));
        p.RecomputeSettlement(); // partially paid → still Scheduled, not auto-cancellable

        Assert.Equal(PayableStatus.Scheduled, p.Status);
        Assert.Throws<InvalidOperationException>(() => p.Cancel("erro"));
    }

    [Fact]
    public void Cancel_allowed_when_a_payment_was_reversed()
    {
        var p = NewPayable(1000m);
        p.Approve("u1", At);
        var inst = p.Installments[0];
        var pay = inst.RegisterPayment(new Payment(inst.Id, p.Id, new DateOnly(2026, 7, 10), 400m, Guid.NewGuid(),
            PaymentMethod.Pix, 0, 0, 0, null, Guid.NewGuid(), default));
        inst.ReversePayment(pay.Id, "estorno");
        p.RecomputeSettlement();

        // only reversed payments remain → cancelling is allowed again
        p.Cancel("duplicada");
        Assert.Equal(PayableStatus.Cancelled, p.Status);
        Assert.Equal("duplicada", p.CancelReason);
    }

    [Fact]
    public void Approve_from_received_succeeds_and_is_idempotent()
    {
        var p = NewPayable(100m);
        p.Approve("u1", At);
        Assert.Equal(PayableStatus.PendingAccounting, p.Status);
        p.Approve("u1", At);
        Assert.Equal(PayableStatus.PendingAccounting, p.Status);
    }

    [Fact]
    public void Approve_records_approver_and_timestamp()
    {
        var p = NewPayable(100m);
        p.Approve("u1", At);
        Assert.Equal("u1", p.ApprovedBy);
        Assert.Equal(At, p.ApprovedAt);
        Assert.Null(p.RejectedBy);
        Assert.Null(p.RejectedAt);
    }

    [Fact]
    public void Reject_records_rejecter_and_timestamp()
    {
        var p = NewPayable(100m);
        p.Reject("motivo", "u2", At);
        Assert.Equal("u2", p.RejectedBy);
        Assert.Equal(At, p.RejectedAt);
    }

    [Fact]
    public void Reopen_returns_rejected_payable_to_received_and_clears_stamps()
    {
        var p = NewPayable(100m);
        p.Reject("doc faltando", "u1", At);
        p.Reopen();
        Assert.Equal(PayableStatus.Received, p.Status);
        Assert.Null(p.RejectionReason);
        Assert.Null(p.RejectedBy);
        Assert.Null(p.RejectedAt);
    }

    [Fact]
    public void Reopen_resets_the_accounting_classification_so_it_re_enters_the_review()
    {
        // A classified + accounted title that gets returned must, on re-approval, be re-classifiable — the
        // Contábil review queue keys on AccountingAccountId == null (AEL-935).
        var p = NewPayable(100m);
        p.SetSuggestion(Guid.NewGuid(), 0.6, "maybe");
        p.Classify(Guid.NewGuid());
        p.Approve("u1", At);
        p.CompleteAccounting("acct", At, hasFiscalDocument: false, allowWithoutFiscalDocument: true); // → Scheduled, stamps AccountedBy
        p.Reject("valor divergente", "u1", At);

        p.Reopen();

        Assert.False(p.IsClassified);
        Assert.Null(p.AccountingAccountId);
        Assert.Null(p.SuggestedAccountingAccountId);
        Assert.Null(p.ClassificationConfidence);
        Assert.Null(p.ClassificationRationale);
        Assert.Null(p.AccountedBy);
        Assert.Null(p.AccountedAt);
    }

    [Fact]
    public void Reopen_throws_when_not_rejected()
    {
        var p = NewPayable(100m);
        Assert.Throws<InvalidOperationException>(p.Reopen);
    }

    [Fact]
    public void SetDocumentNumber_sets_and_trims_value()
    {
        var p = NewPayable(100m);
        p.SetDocumentNumber("  NF-42  ");
        Assert.Equal("NF-42", p.DocumentNumber);
    }

    [Fact]
    public void SetDocumentNumber_normalizes_blank_to_null()
    {
        var p = NewPayable(100m);
        p.SetDocumentNumber("   ");
        Assert.Null(p.DocumentNumber);
    }

    [Fact]
    public void SetDocumentNumber_allowed_when_rejected_or_cancelled()
    {
        // Fiscal metadata (SourceReference) — correctable even in terminal states; never affects amounts.
        var rejected = NewPayable(100m);
        rejected.Reject("x", "u1", At);
        rejected.SetDocumentNumber("NF-9");
        Assert.Equal("NF-9", rejected.DocumentNumber);

        var cancelled = NewPayable(100m);
        cancelled.Cancel("x");
        cancelled.SetDocumentNumber("NF-9");
        Assert.Equal("NF-9", cancelled.DocumentNumber);
    }

    [Fact]
    public void SetDocumentNumber_allowed_when_paid()
    {
        // Contábil often only gets the número after payment — a paid payable must still accept it.
        var p = NewPayable(100m);
        p.Approve("u1", At);
        p.CompleteAccounting("u1", At, hasFiscalDocument: true); // → Scheduled (payable)
        var inst = p.Installments[0];
        inst.RegisterPayment(new Payment(inst.Id, p.Id, new DateOnly(2026, 7, 10), 100m, Guid.NewGuid(),
            PaymentMethod.Pix, 0, 0, 0, null, Guid.NewGuid(), default));
        p.RecomputeSettlement();
        Assert.Equal(PayableStatus.Paid, p.Status);
        p.SetDocumentNumber("NF-PAID");
        Assert.Equal("NF-PAID", p.DocumentNumber);
    }
}
