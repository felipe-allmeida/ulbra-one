namespace Ulbra.One.Finance.Domain.Entities;

/// <summary>A tax withholding (retenção na fonte) applied to a payable — e.g. IRRF, ISS, INSS.</summary>
public class PayableWithholding
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid PayableId { get; private set; }
    public WithholdingTaxType TaxType { get; private set; }
    public decimal BaseAmount { get; private set; }
    public decimal? Rate { get; private set; }
    public decimal Amount { get; private set; }

    private PayableWithholding() { }

    internal PayableWithholding(Guid payableId, WithholdingTaxType taxType, decimal baseAmount, decimal? rate, decimal amount)
    {
        PayableId = payableId;
        TaxType = taxType;
        BaseAmount = baseAmount;
        Rate = rate;
        Amount = amount;
    }
}
