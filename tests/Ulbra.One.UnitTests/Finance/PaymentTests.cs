using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Xunit;

namespace Ulbra.One.UnitTests.Finance;

public class PaymentTests
{
    private static Payable ApprovedPayable(decimal total, int count = 1)
    {
        var p = new Payable(Guid.NewGuid(), PayableOrigin.Manual, Guid.NewGuid(), "CEEE", null, null, "Energia",
            Guid.NewGuid(), null, Payable.GenerateInstallments(total, count, new DateOnly(2026, 7, 10), 1));
        p.Approve();
        return p;
    }

    private static Payment NewPayment(Installment inst, decimal amount, decimal interest = 0, decimal fine = 0, decimal discount = 0) =>
        new(inst.Id, Guid.NewGuid(), new DateOnly(2026, 7, 10), amount, Guid.NewGuid(), PaymentMethod.Pix, interest, fine, discount, null, Guid.NewGuid(), default);

    [Fact]
    public void Principal_applied_nets_interest_fine_discount()
    {
        var inst = ApprovedPayable(1000m).Installments[0];
        Assert.Equal(1000m, NewPayment(inst, amount: 1050m, interest: 50m).PrincipalApplied);
        Assert.Equal(1000m, NewPayment(inst, amount: 980m, discount: 20m).PrincipalApplied);
    }

    [Fact]
    public void Full_payment_marks_installment_paid_and_payable_settled()
    {
        var payable = ApprovedPayable(1000m);
        var inst = payable.Installments[0];
        inst.RegisterPayment(NewPayment(inst, 1000m));
        payable.RecomputeSettlement();
        Assert.Equal(InstallmentStatus.Paid, inst.Status);
        Assert.Equal(1000m, inst.PaidAmount);
        Assert.Equal(PayableStatus.Settled, payable.Status);
    }

    [Fact]
    public void Partial_payment_marks_partially_paid_and_keeps_approved()
    {
        var payable = ApprovedPayable(1000m);
        var inst = payable.Installments[0];
        inst.RegisterPayment(NewPayment(inst, 400m));
        payable.RecomputeSettlement();
        Assert.Equal(InstallmentStatus.PartiallyPaid, inst.Status);
        Assert.Equal(400m, inst.PaidAmount);
        Assert.Equal(PayableStatus.Approved, payable.Status);
    }

    [Fact]
    public void Payable_settles_only_when_all_installments_paid()
    {
        var payable = ApprovedPayable(1000m, count: 2);
        var i0 = payable.Installments[0];
        i0.RegisterPayment(NewPayment(i0, 500m));
        payable.RecomputeSettlement();
        Assert.Equal(PayableStatus.Approved, payable.Status);
        var i1 = payable.Installments[1];
        i1.RegisterPayment(NewPayment(i1, 500m));
        payable.RecomputeSettlement();
        Assert.Equal(PayableStatus.Settled, payable.Status);
    }

    [Fact]
    public void Registering_on_a_paid_installment_throws()
    {
        var inst = ApprovedPayable(1000m).Installments[0];
        inst.RegisterPayment(NewPayment(inst, 1000m));
        Assert.Throws<InvalidOperationException>(() => inst.RegisterPayment(NewPayment(inst, 100m)));
    }

    [Fact]
    public void Reversing_a_payment_reopens_the_installment_and_unsettles_payable()
    {
        var payable = ApprovedPayable(1000m);
        var inst = payable.Installments[0];
        var pay = inst.RegisterPayment(NewPayment(inst, 1000m));
        payable.RecomputeSettlement();
        Assert.Equal(PayableStatus.Settled, payable.Status);
        inst.ReversePayment(pay.Id, "conta errada");
        payable.RecomputeSettlement();
        Assert.Equal(InstallmentStatus.Open, inst.Status);
        Assert.Equal(0m, inst.PaidAmount);
        Assert.Equal(PayableStatus.Approved, payable.Status);
        Assert.True(pay.IsReversed);
    }

    [Fact]
    public void Reversing_an_already_reversed_payment_throws()
    {
        var inst = ApprovedPayable(1000m).Installments[0];
        var pay = inst.RegisterPayment(NewPayment(inst, 1000m));
        inst.ReversePayment(pay.Id, "x");
        Assert.Throws<InvalidOperationException>(() => inst.ReversePayment(pay.Id, "y"));
    }
}
