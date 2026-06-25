using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Xunit;

namespace Ulbra.One.UnitTests.Finance;

public class PayableWithholdingTests
{
    private static readonly DateTimeOffset At = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    private static Payable NewPayable(decimal total, int count = 1) =>
        new(Guid.NewGuid(), PayableOrigin.Manual, Guid.NewGuid(), "CEEE",
            documentNumber: "NF-1", issueDate: new DateOnly(2026, 7, 1), description: "Energia",
            costCenterId: Guid.NewGuid(), categoryId: null,
            Payable.GenerateInstallments(total, count, new DateOnly(2026, 7, 10), 1));

    // (a) Single-installment payable — one IRRF withholding of 150 on 1000
    [Fact]
    public void SetWithholdings_single_installment_updates_totals_and_net_amount()
    {
        var p = NewPayable(1000m, 1);

        p.SetWithholdings([(WithholdingTaxType.Irrf, 1000m, 0.15m, 150m)]);

        Assert.Equal(150m, p.TotalWithheld);
        Assert.Equal(850m, p.NetTotal);
        var w = Assert.Single(p.Withholdings);
        Assert.Equal(WithholdingTaxType.Irrf, w.TaxType);
        Assert.Equal(850m, p.Installments[0].NetAmount);
    }

    // (b) Two installments 600/400 on 1000 with 100 withheld → net 900 → NetAmount 540/360
    [Fact]
    public void SetWithholdings_two_installments_distributes_net_proportionally_and_last_absorbs_rounding()
    {
        // Use a 3-installment case that forces rounding: 1000 total, 3 installments of equal 333.34/333.33/333.33
        // Withhold 1 → net = 999; each raw = 333.33 (inst1) → 333.00, inst2 → 333.00, last = 999-333-333-333 = 0? No.
        // Let's use 2 installments 600/400 on 1000 with 100 withheld → net 900 → 540/360, no rounding needed.
        // And additionally test rounding with 3 equal installments of ~333.33 each.

        // Two installments: 600 and 400 on 1000 total, withhold 100
        var installments = new List<Installment>
        {
            new(1, new DateOnly(2026, 7, 10), 600m),
            new(2, new DateOnly(2026, 8, 10), 400m),
        };
        var p = new Payable(Guid.NewGuid(), PayableOrigin.Manual, Guid.NewGuid(), "CEEE",
            "NF-1", new DateOnly(2026, 7, 1), "Energia", Guid.NewGuid(), null, installments);

        p.SetWithholdings([(WithholdingTaxType.Iss, 1000m, 0.1m, 100m)]);

        Assert.Equal(100m, p.TotalWithheld);
        Assert.Equal(900m, p.NetTotal);
        // inst1 = Math.Round(600 * 900 / 1000, 2) = Math.Round(540.00, 2) = 540
        Assert.Equal(540m, p.Installments[0].NetAmount);
        // last = 900 - 540 = 360
        Assert.Equal(360m, p.Installments[1].NetAmount);
        Assert.Equal(900m, p.Installments.Sum(i => i.NetAmount));
    }

    // Rounding case: 3 equal installments (~333.33 each) forcing last-absorbs-rounding
    [Fact]
    public void SetWithholdings_three_installments_last_absorbs_rounding()
    {
        // Total 1000, 3 installments: 334 + 333 + 333 = 1000
        // Withhold 100 → net = 900
        // inst1: Round(334 * 900 / 1000, 2) = Round(300.60, 2) = 300.60
        // inst2: Round(333 * 900 / 1000, 2) = Round(299.70, 2) = 299.70
        // inst3 (last): 900 - 300.60 - 299.70 = 299.70
        var installments = Payable.GenerateInstallments(1000m, 3, new DateOnly(2026, 7, 10), 1);
        // GenerateInstallments: each=333.33, last=1000-666.66=333.34 (remainder on last by convention)
        // Actually: each = Round(1000/3, 2, AwayFromZero) = 333.34? Let's check:
        // Math.Round(1000m/3, 2, MidpointRounding.AwayFromZero) = Math.Round(333.333..., 2, AwayFromZero) = 333.34? No: 333.33 rounds to 333.33, 333.333... → 333.33 (digit after 2nd decimal = 3, not ≥5, stays 333.33)
        // each=333.33, acc after 2=666.66, last=1000-666.66=333.34
        // Withhold 1 unit → net=999
        // inst1: Round(333.33 * 999 / 1000, 2) = Round(333.00, 2)? 333.33*999=333,000-333.33*1=333.33*999=332,996.67/1000=332.99667 → Round=333.00
        // Hmm, let me use simpler numbers:
        // Use 3 installments: 100/100/100 = 300 total, withhold 1 → net=299
        // inst1: Round(100*299/300,2) = Round(99.666...,2) = 99.67
        // inst2: Round(100*299/300,2) = 99.67
        // inst3: 299 - 99.67 - 99.67 = 99.66
        // Σ = 99.67+99.67+99.66 = 299 ✓
        var insts = new List<Installment>
        {
            new(1, new DateOnly(2026, 7, 10), 100m),
            new(2, new DateOnly(2026, 8, 10), 100m),
            new(3, new DateOnly(2026, 9, 10), 100m),
        };
        var p = new Payable(Guid.NewGuid(), PayableOrigin.Manual, Guid.NewGuid(), "CEEE",
            "NF-2", new DateOnly(2026, 7, 1), "Servicos", Guid.NewGuid(), null, insts);

        p.SetWithholdings([(WithholdingTaxType.Csll, 300m, null, 1m)]);

        Assert.Equal(1m, p.TotalWithheld);
        Assert.Equal(299m, p.NetTotal);
        Assert.Equal(99.67m, p.Installments[0].NetAmount);
        Assert.Equal(99.67m, p.Installments[1].NetAmount);
        Assert.Equal(99.66m, p.Installments[2].NetAmount);
        Assert.Equal(299m, p.Installments.Sum(i => i.NetAmount));
    }

    // (c) Clearing withholdings → NetAmount == Amount
    [Fact]
    public void SetWithholdings_empty_resets_net_amounts_to_gross()
    {
        var p = NewPayable(1000m, 2);
        p.SetWithholdings([(WithholdingTaxType.Irrf, 1000m, 0.15m, 150m)]);

        // Now clear
        p.SetWithholdings([]);

        Assert.Equal(0m, p.TotalWithheld);
        Assert.Equal(1000m, p.NetTotal);
        Assert.All(p.Installments, i => Assert.Equal(i.Amount, i.NetAmount));
    }

    // (d) Σ Amount >= TotalAmount throws
    [Fact]
    public void SetWithholdings_throws_when_total_withheld_equals_total_amount()
    {
        var p = NewPayable(1000m);

        Assert.Throws<InvalidOperationException>(() =>
            p.SetWithholdings([(WithholdingTaxType.Irrf, 1000m, 1m, 1000m)]));
    }

    [Fact]
    public void SetWithholdings_throws_when_total_withheld_exceeds_total_amount()
    {
        var p = NewPayable(1000m);

        Assert.Throws<InvalidOperationException>(() =>
            p.SetWithholdings([(WithholdingTaxType.Irrf, 1000m, null, 1500m)]));
    }

    [Fact]
    public void SetWithholdings_throws_when_any_amount_is_zero_or_negative()
    {
        var p = NewPayable(1000m);

        Assert.Throws<InvalidOperationException>(() =>
            p.SetWithholdings([(WithholdingTaxType.Irrf, 1000m, null, 0m)]));

        Assert.Throws<InvalidOperationException>(() =>
            p.SetWithholdings([(WithholdingTaxType.Irrf, 1000m, null, -10m)]));
    }

    // (e) SetWithholdings on a payable with a payment throws (EnsureMutable guard)
    [Fact]
    public void SetWithholdings_throws_when_payable_has_a_registered_payment()
    {
        var p = NewPayable(1000m);
        p.Approve("u1", At);
        var inst = p.Installments[0];
        inst.RegisterPayment(new Payment(inst.Id, p.Id, new DateOnly(2026, 7, 10), 1000m, Guid.NewGuid(),
            PaymentMethod.Pix, 0, 0, 0, null, Guid.NewGuid(), default));

        Assert.Throws<InvalidOperationException>(() =>
            p.SetWithholdings([(WithholdingTaxType.Irrf, 1000m, 0.15m, 150m)]));
    }

    // NetAmount defaults to Amount on fresh installment
    [Fact]
    public void Installment_net_amount_defaults_to_gross_amount()
    {
        var i = new Installment(1, new DateOnly(2026, 7, 10), 500m);
        Assert.Equal(500m, i.NetAmount);
    }

    // Multiple withholdings summed correctly
    [Fact]
    public void SetWithholdings_multiple_types_sums_all()
    {
        var p = NewPayable(1000m, 1);

        p.SetWithholdings([
            (WithholdingTaxType.Irrf, 1000m, 0.05m, 50m),
            (WithholdingTaxType.Inss, 1000m, 0.03m, 30m),
            (WithholdingTaxType.Iss, 1000m, 0.02m, 20m),
        ]);

        Assert.Equal(100m, p.TotalWithheld);
        Assert.Equal(900m, p.NetTotal);
        Assert.Equal(3, p.Withholdings.Count);
        Assert.Equal(900m, p.Installments[0].NetAmount);
    }
}
