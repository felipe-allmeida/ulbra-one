using Ulbra.One.Finance.Domain;

namespace Ulbra.One.UnitTests.Finance;

public class WithholdingTimingsTests
{
    [Theory]
    // CSRF (DARF 5952) — fato gerador no pagamento
    [InlineData(WithholdingTaxType.Pis, WithholdingTiming.OnPayment)]
    [InlineData(WithholdingTaxType.Cofins, WithholdingTiming.OnPayment)]
    [InlineData(WithholdingTaxType.Csll, WithholdingTiming.OnPayment)]
    // Emissão / competência
    [InlineData(WithholdingTaxType.Irrf, WithholdingTiming.OnIssue)]
    [InlineData(WithholdingTaxType.Inss, WithholdingTiming.OnIssue)]
    [InlineData(WithholdingTaxType.Iss, WithholdingTiming.OnIssue)]
    [InlineData(WithholdingTaxType.Other, WithholdingTiming.OnIssue)]
    public void For_maps_tax_to_recognition_moment(WithholdingTaxType tax, WithholdingTiming expected) =>
        Assert.Equal(expected, WithholdingTimings.For(tax));
}
