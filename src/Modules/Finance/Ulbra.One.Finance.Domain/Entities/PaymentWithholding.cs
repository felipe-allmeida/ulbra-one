namespace Ulbra.One.Finance.Domain.Entities;

/// <summary>A payment-time tax withholding (CSRF — PIS/COFINS/CSLL) recognized at a specific baixa, prorated to
/// the amount paid. Unlike <see cref="PayableWithholding"/> (document-level, competência), this is the realized
/// retention per <see cref="Payment"/>: it is the source of truth for the settlement journal entry and for the
/// fiscal assessment by payment date. Reversed together with its payment (estorno).</summary>
public class PaymentWithholding
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid PaymentId { get; private set; }
    public WithholdingTaxType TaxType { get; private set; }
    // Calculation base prorated to this payment (informational — feeds the fiscal assessment/guia base column).
    public decimal BaseAmount { get; private set; }
    public decimal Amount { get; private set; }

    private PaymentWithholding() { }

    internal PaymentWithholding(Guid paymentId, WithholdingTaxType taxType, decimal baseAmount, decimal amount)
    {
        PaymentId = paymentId;
        TaxType = taxType;
        BaseAmount = baseAmount;
        Amount = amount;
    }
}
