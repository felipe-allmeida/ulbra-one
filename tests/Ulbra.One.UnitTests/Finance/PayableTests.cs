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
    public void New_payable_totals_installments_and_starts_pending()
    {
        var p = NewPayable(100m, 4);
        Assert.Equal(100m, p.TotalAmount);
        Assert.Equal(4, p.Installments.Count);
        Assert.Equal(PayableStatus.PendingApproval, p.Status);
        Assert.All(p.Installments, i => Assert.Equal(InstallmentStatus.Open, i.Status));
    }

    [Fact]
    public void New_payable_rejects_zero_installments()
    {
        Assert.Throws<ArgumentException>(() =>
            new Payable(Guid.NewGuid(), PayableOrigin.Manual, Guid.NewGuid(), "X", null, null, "d",
                Guid.NewGuid(), null, new List<Installment>()));
    }

    [Fact]
    public void Auto_approval_approves_when_total_within_limit()
    {
        var p = NewPayable(500m);
        p.ApplyAutoApproval(1000m, At);
        Assert.Equal(PayableStatus.Approved, p.Status);
        Assert.Equal("system", p.ApprovedBy);
        Assert.Equal(At, p.ApprovedAt);
    }

    [Fact]
    public void Auto_approval_keeps_pending_when_above_limit()
    {
        var p = NewPayable(1500m);
        p.ApplyAutoApproval(1000m, At);
        Assert.Equal(PayableStatus.PendingApproval, p.Status);
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
    public void Approve_from_pending_succeeds_and_is_idempotent()
    {
        var p = NewPayable(100m);
        p.Approve("u1", At);
        Assert.Equal(PayableStatus.Approved, p.Status);
        p.Approve("u1", At);
        Assert.Equal(PayableStatus.Approved, p.Status);
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
}
