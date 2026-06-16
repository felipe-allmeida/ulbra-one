using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Xunit;

namespace Ulbra.One.UnitTests.Finance;

public class ReceiptTests
{
    private static Receivable NewReceivable(decimal total, int count = 1) =>
        new(Guid.NewGuid(), ReceivableOrigin.Manual, Guid.NewGuid(), "Maria Eduarda", null, null, "Mensalidade",
            Guid.NewGuid(), null, null, Receivable.GenerateInstallments(total, count, new DateOnly(2026, 7, 5), 1));

    private static Receipt NewReceipt(ReceivableInstallment inst, decimal amount, decimal interest = 0, decimal fine = 0, decimal discount = 0) =>
        new(inst.Id, Guid.NewGuid(), new DateOnly(2026, 7, 5), amount, Guid.NewGuid(), PaymentMethod.Pix, interest, fine, discount, null, Guid.NewGuid(), default);

    [Fact]
    public void Principal_applied_nets_interest_fine_discount()
    {
        var inst = NewReceivable(1000m).Installments[0];
        Assert.Equal(1000m, NewReceipt(inst, amount: 1050m, interest: 50m).PrincipalApplied);
        Assert.Equal(1000m, NewReceipt(inst, amount: 980m, discount: 20m).PrincipalApplied);
    }

    [Fact]
    public void Receivable_is_born_awaiting_receipt_without_approval()
    {
        Assert.Equal(ReceivableStatus.AwaitingReceipt, NewReceivable(1000m).Status);
    }

    [Fact]
    public void Full_receipt_marks_installment_received_and_receivable_settled()
    {
        var r = NewReceivable(1000m);
        var inst = r.Installments[0];
        inst.RegisterReceipt(NewReceipt(inst, 1000m));
        r.RecomputeSettlement();
        Assert.Equal(ReceivableInstallmentStatus.Received, inst.Status);
        Assert.Equal(1000m, inst.ReceivedAmount);
        Assert.Equal(ReceivableStatus.Received, r.Status);
    }

    [Fact]
    public void Partial_receipt_marks_partially_received_and_keeps_awaiting()
    {
        var r = NewReceivable(1000m);
        var inst = r.Installments[0];
        inst.RegisterReceipt(NewReceipt(inst, 400m));
        r.RecomputeSettlement();
        Assert.Equal(ReceivableInstallmentStatus.PartiallyReceived, inst.Status);
        Assert.Equal(400m, inst.ReceivedAmount);
        Assert.Equal(ReceivableStatus.AwaitingReceipt, r.Status);
    }

    [Fact]
    public void Receivable_settles_only_when_all_installments_received()
    {
        var r = NewReceivable(1000m, count: 2);
        var i0 = r.Installments[0];
        i0.RegisterReceipt(NewReceipt(i0, 500m));
        r.RecomputeSettlement();
        Assert.Equal(ReceivableStatus.AwaitingReceipt, r.Status);
        var i1 = r.Installments[1];
        i1.RegisterReceipt(NewReceipt(i1, 500m));
        r.RecomputeSettlement();
        Assert.Equal(ReceivableStatus.Received, r.Status);
    }

    [Fact]
    public void Registering_on_a_received_installment_throws()
    {
        var inst = NewReceivable(1000m).Installments[0];
        inst.RegisterReceipt(NewReceipt(inst, 1000m));
        Assert.Throws<InvalidOperationException>(() => inst.RegisterReceipt(NewReceipt(inst, 100m)));
    }

    [Fact]
    public void Over_receipt_beyond_outstanding_balance_throws()
    {
        var inst = NewReceivable(1000m).Installments[0];
        inst.RegisterReceipt(NewReceipt(inst, 600m));
        Assert.Throws<InvalidOperationException>(() => inst.RegisterReceipt(NewReceipt(inst, 600m)));
    }

    [Fact]
    public void Reversing_a_receipt_reopens_the_installment_unsettles_and_records_audit()
    {
        var r = NewReceivable(1000m);
        var inst = r.Installments[0];
        var rcpt = inst.RegisterReceipt(NewReceipt(inst, 1000m));
        r.RecomputeSettlement();
        Assert.Equal(ReceivableStatus.Received, r.Status);

        var who = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);
        inst.ReverseReceipt(rcpt.Id, who, at, "conta errada");
        r.RecomputeSettlement();

        Assert.Equal(ReceivableInstallmentStatus.Open, inst.Status);
        Assert.Equal(0m, inst.ReceivedAmount);
        Assert.Equal(ReceivableStatus.AwaitingReceipt, r.Status);
        Assert.True(rcpt.IsReversed);
        Assert.Equal(who, rcpt.ReversedById);
        Assert.Equal(at, rcpt.ReversedAt);
    }

    [Fact]
    public void Reversing_an_already_reversed_receipt_throws()
    {
        var inst = NewReceivable(1000m).Installments[0];
        var rcpt = inst.RegisterReceipt(NewReceipt(inst, 1000m));
        inst.ReverseReceipt(rcpt.Id, Guid.NewGuid(), default, "x");
        Assert.Throws<InvalidOperationException>(() => inst.ReverseReceipt(rcpt.Id, Guid.NewGuid(), default, "y"));
    }

    [Fact]
    public void Cancel_with_receipts_throws_but_clean_cancel_records_audit()
    {
        var withReceipts = NewReceivable(1000m);
        var inst = withReceipts.Installments[0];
        inst.RegisterReceipt(NewReceipt(inst, 400m));
        withReceipts.RecomputeSettlement();
        Assert.Throws<InvalidOperationException>(() => withReceipts.Cancel(Guid.NewGuid(), default, "desistiu"));

        var clean = NewReceivable(1000m);
        var who = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 7, 21, 9, 0, 0, TimeSpan.Zero);
        clean.Cancel(who, at, "lançado errado");
        Assert.Equal(ReceivableStatus.Cancelled, clean.Status);
        Assert.Equal(who, clean.CancelledById);
        Assert.Equal(at, clean.CancelledAt);
    }

    [Fact]
    public void Receipt_with_non_positive_principal_applied_throws()
    {
        var inst = NewReceivable(1000m).Installments[0];
        // Amount fully offset by interest+fine → PrincipalApplied == 0.
        Assert.Throws<InvalidOperationException>(() => inst.RegisterReceipt(NewReceipt(inst, amount: 100m, interest: 60m, fine: 40m)));
    }

    [Fact]
    public void Installments_sum_to_total_with_remainder_on_last()
    {
        var r = NewReceivable(100m, count: 3);
        Assert.Equal(100m, r.TotalAmount);
        Assert.Equal(new[] { 33.33m, 33.33m, 33.34m }, r.Installments.Select(i => i.Amount).ToArray());
    }
}
