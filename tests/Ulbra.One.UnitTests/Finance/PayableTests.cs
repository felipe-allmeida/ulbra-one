using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Xunit;

namespace Ulbra.One.UnitTests.Finance;

public class PayableTests
{
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
        p.ApplyAutoApproval(1000m);
        Assert.Equal(PayableStatus.Approved, p.Status);
    }

    [Fact]
    public void Auto_approval_keeps_pending_when_above_limit()
    {
        var p = NewPayable(1500m);
        p.ApplyAutoApproval(1000m);
        Assert.Equal(PayableStatus.PendingApproval, p.Status);
    }

    [Fact]
    public void Reject_sets_reason_and_state()
    {
        var p = NewPayable(100m);
        p.Reject("doc faltando");
        Assert.Equal(PayableStatus.Rejected, p.Status);
        Assert.Equal("doc faltando", p.RejectionReason);
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
        p.Approve();
        Assert.Equal(PayableStatus.Approved, p.Status);
        p.Approve();
        Assert.Equal(PayableStatus.Approved, p.Status);
    }
}
