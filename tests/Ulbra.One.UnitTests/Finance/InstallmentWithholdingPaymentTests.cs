using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Xunit;

namespace Ulbra.One.UnitTests.Finance;

/// <summary>
/// Verifies that <see cref="Installment"/> settles at <see cref="Installment.NetAmount"/>
/// (the cash obligation to the supplier) rather than the gross <see cref="Installment.Amount"/>.
/// </summary>
public class InstallmentWithholdingPaymentTests
{
    private static readonly Guid AnyBankAccountId = Guid.NewGuid();
    private static readonly DateOnly AnyDate = new(2026, 7, 1);
    private static readonly DateTimeOffset AnyAt = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    // Builds a Payment using zero interest/fine/discount so PrincipalApplied == amount.
    private static Payment MakePayment(Guid installmentId, Guid payableId, decimal amount) =>
        new(installmentId, payableId, AnyDate, amount, AnyBankAccountId,
            PaymentMethod.Pix, interest: 0m, fine: 0m, discount: 0m, notes: null,
            createdById: Guid.NewGuid(), createdAt: default);

    /// <summary>
    /// Creates a single-installment <see cref="Payable"/> with an IRRF withholding of
    /// <paramref name="withheld"/> on a gross <paramref name="gross"/>, then approves it so
    /// the installment is payable. Returns the ready (Scheduled) payable.
    /// </summary>
    private static Payable ApprovedWithheldPayable(decimal gross, decimal withheld)
    {
        var payable = new Payable(
            Guid.NewGuid(), PayableOrigin.Manual, Guid.NewGuid(), "Fornecedor SA",
            documentNumber: "NF-1", issueDate: AnyDate, description: "Servicos",
            costCenterId: Guid.NewGuid(), categoryId: null,
            Payable.GenerateInstallments(gross, 1, AnyDate, 1));

        payable.SetWithholdings([(WithholdingTaxType.Irrf, BaseAmount: gross, Rate: null, Amount: withheld)]);
        payable.Approve("test-user", AnyAt);
        return payable;
    }

    // ---------------------------------------------------------------------------
    // Withheld-installment scenarios
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Gross 1000, net 850 (IRRF 150): paying exactly 850 must reach <c>Paid</c>.
    /// </summary>
    [Fact]
    public void Paying_NetAmount_on_withheld_installment_settles_to_Paid()
    {
        var payable = ApprovedWithheldPayable(gross: 1000m, withheld: 150m);
        var inst = payable.Installments[0];
        Assert.Equal(850m, inst.NetAmount);

        inst.RegisterPayment(MakePayment(inst.Id, payable.Id, 850m));

        Assert.Equal(InstallmentStatus.Paid, inst.Status);
        Assert.Equal(850m, inst.PaidAmount);
    }

    /// <summary>
    /// Paying 849 (one cent below net) must leave the installment <c>PartiallyPaid</c>.
    /// </summary>
    [Fact]
    public void Paying_below_NetAmount_on_withheld_installment_stays_PartiallyPaid()
    {
        var payable = ApprovedWithheldPayable(gross: 1000m, withheld: 150m);
        var inst = payable.Installments[0];

        inst.RegisterPayment(MakePayment(inst.Id, payable.Id, 849m));

        Assert.Equal(InstallmentStatus.PartiallyPaid, inst.Status);
    }

    /// <summary>
    /// Paying 851 (above net 850) must throw — the supplier is owed only the net amount.
    /// No payment must be recorded.
    /// </summary>
    [Fact]
    public void Paying_above_NetAmount_on_withheld_installment_throws_overpayment()
    {
        var payable = ApprovedWithheldPayable(gross: 1000m, withheld: 150m);
        var inst = payable.Installments[0];

        Assert.Throws<InvalidOperationException>(() =>
            inst.RegisterPayment(MakePayment(inst.Id, payable.Id, 851m)));

        Assert.Empty(inst.Payments);
    }

    // ---------------------------------------------------------------------------
    // Regression: no-withholding installment (NetAmount == Amount)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// When there is no withholding, <c>NetAmount == Amount</c>.
    /// Paying the full gross amount must still settle the installment to <c>Paid</c>.
    /// </summary>
    [Fact]
    public void Paying_full_Amount_on_no_withholding_installment_settles_to_Paid()
    {
        var inst = new Installment(1, AnyDate, 500m);
        Assert.Equal(500m, inst.NetAmount); // default: NetAmount == Amount

        inst.RegisterPayment(MakePayment(inst.Id, Guid.NewGuid(), 500m));

        Assert.Equal(InstallmentStatus.Paid, inst.Status);
    }

    // ---------------------------------------------------------------------------
    // Payable reaches Paid after net payment + RecomputeSettlement
    // ---------------------------------------------------------------------------

    /// <summary>
    /// After the auto-pay registers a payment for <c>NetAmount</c> on a withheld payable,
    /// calling <see cref="Payable.RecomputeSettlement"/> must move the payable to <c>Paid</c>.
    /// </summary>
    [Fact]
    public void Withheld_payable_reaches_Paid_after_net_payment_and_recompute()
    {
        var payable = ApprovedWithheldPayable(gross: 1000m, withheld: 150m);
        var inst = payable.Installments[0];
        Assert.Equal(850m, inst.NetAmount);

        inst.RegisterPayment(MakePayment(inst.Id, payable.Id, 850m));

        Assert.Equal(InstallmentStatus.Paid, inst.Status);

        payable.RecomputeSettlement();

        Assert.Equal(PayableStatus.Paid, payable.Status);
    }
}
