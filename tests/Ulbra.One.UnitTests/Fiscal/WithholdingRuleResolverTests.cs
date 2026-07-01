using Ulbra.One.Finance.Domain;
using Ulbra.One.Fiscal.Domain;
using Ulbra.One.Fiscal.Domain.Entities;
using Ulbra.One.Fiscal.Domain.Withholdings;
using Ulbra.One.Registry.Domain.Entities;
using Xunit;

namespace Ulbra.One.UnitTests.Fiscal;

/// <summary>Acceptance table for the automatic withholding calculation (HTML §4.5).</summary>
public class WithholdingRuleResolverTests
{
    // Baseline rule set (mirrors the seed the Accounting team validates).
    private static List<WithholdingRule> BaseRules() =>
    [
        // General federal taxes (any regime).
        new(WithholdingTaxType.Irrf, retains: true, RateSource.Fixed, 0.015m),
        new(WithholdingTaxType.Pis, retains: true, RateSource.Fixed, 0.0065m),
        new(WithholdingTaxType.Cofins, retains: true, RateSource.Fixed, 0.03m),
        new(WithholdingTaxType.Csll, retains: true, RateSource.Fixed, 0.01m),
        // Simples exemption (federal income/contribution taxes) — conditioned on the declaration.
        new(WithholdingTaxType.Irrf, retains: false, RateSource.Fixed, null, vendorRegime: TaxRegime.SimplesNacional, reason: NonRetentionReason.RegimeExemption),
        new(WithholdingTaxType.Pis, retains: false, RateSource.Fixed, null, vendorRegime: TaxRegime.SimplesNacional, reason: NonRetentionReason.RegimeExemption),
        new(WithholdingTaxType.Cofins, retains: false, RateSource.Fixed, null, vendorRegime: TaxRegime.SimplesNacional, reason: NonRetentionReason.RegimeExemption),
        new(WithholdingTaxType.Csll, retains: false, RateSource.Fixed, null, vendorRegime: TaxRegime.SimplesNacional, reason: NonRetentionReason.RegimeExemption),
        // INSS 11% for labor-supply services (LC116 subitem 11.02 = security) — any regime.
        new(WithholdingTaxType.Inss, retains: true, RateSource.Fixed, 0.11m, lc116Subitem: "11.02"),
        // ISS: non-Simples uses the municipal rate (Canoas 5%); Simples uses the invoice rate (band).
        new(WithholdingTaxType.Iss, retains: true, RateSource.Fixed, 0.05m, municipalityIbge: "4304606"),
        new(WithholdingTaxType.Iss, retains: true, RateSource.FromNota, null, vendorRegime: TaxRegime.SimplesNacional),
    ];

    private static decimal Amount(IReadOnlyList<WithholdingComputation> r, WithholdingTaxType t) =>
        r.Where(c => c.TaxType == t).Sum(c => c.Amount);

    [Fact]
    public void LucroPresumido_service_withholds_irrf_csrf_and_iss()
    {
        var ctx = new ResolutionContext(null, TaxRegime.LucroPresumido, false,
            [new ServiceLine("17.01", "4304606", null, 3000m)]);

        var r = WithholdingRuleResolver.Resolve(BaseRules(), ctx);

        Assert.Equal(45.00m, Amount(r, WithholdingTaxType.Irrf));    // 1.5%
        Assert.Equal(19.50m, Amount(r, WithholdingTaxType.Pis));     // 0.65%
        Assert.Equal(90.00m, Amount(r, WithholdingTaxType.Cofins));  // 3%
        Assert.Equal(30.00m, Amount(r, WithholdingTaxType.Csll));    // 1%
        Assert.Equal(150.00m, Amount(r, WithholdingTaxType.Iss));    // 5% (Canoas)
        Assert.Equal(0m, Amount(r, WithholdingTaxType.Inss));        // not labor-supply
    }

    [Fact]
    public void Simples_with_declaration_skips_federal_keeps_iss_from_nota()
    {
        var ctx = new ResolutionContext(null, TaxRegime.SimplesNacional, SimplesDeclarationProvided: true,
            [new ServiceLine("17.01", "4304606", 0.03m, 3000m)]); // invoice carries ISS 3% (Simples band)

        var r = WithholdingRuleResolver.Resolve(BaseRules(), ctx);

        Assert.Equal(0m, Amount(r, WithholdingTaxType.Irrf));
        Assert.Equal(0m, Amount(r, WithholdingTaxType.Pis));
        Assert.Equal(0m, Amount(r, WithholdingTaxType.Cofins));
        Assert.Equal(0m, Amount(r, WithholdingTaxType.Csll));
        Assert.Equal(90.00m, Amount(r, WithholdingTaxType.Iss));     // 3% from the invoice
    }

    [Fact]
    public void Simples_without_declaration_withholds_federal()
    {
        var ctx = new ResolutionContext(null, TaxRegime.SimplesNacional, SimplesDeclarationProvided: false,
            [new ServiceLine("17.01", "4304606", 0.03m, 3000m)]);

        var r = WithholdingRuleResolver.Resolve(BaseRules(), ctx);

        Assert.Equal(45.00m, Amount(r, WithholdingTaxType.Irrf));    // exemption falls → withholds
        Assert.Equal(19.50m, Amount(r, WithholdingTaxType.Pis));
        Assert.Equal(90.00m, Amount(r, WithholdingTaxType.Cofins));
        Assert.Equal(30.00m, Amount(r, WithholdingTaxType.Csll));
    }

    [Fact]
    public void Labor_supply_withholds_inss_regardless_of_regime()
    {
        var ctx = new ResolutionContext(null, TaxRegime.SimplesNacional, SimplesDeclarationProvided: true,
            [new ServiceLine("11.02", "4304606", null, 5000m)]); // security

        var r = WithholdingRuleResolver.Resolve(BaseRules(), ctx);

        Assert.Equal(550.00m, Amount(r, WithholdingTaxType.Inss));   // 11% even under Simples
    }

    [Fact]
    public void Service_without_lc116_subitem_still_withholds_federal()
    {
        // The LC116 subitem is a refinement, not the trigger: a service whose subitem wasn't parsed still
        // withholds the general federal taxes. INSS (labor-supply 11.02) and ISS (needs municipality) don't apply.
        var ctx = new ResolutionContext(null, TaxRegime.LucroPresumido, false,
            [new ServiceLine(null, null, null, 3000m)]);

        var r = WithholdingRuleResolver.Resolve(BaseRules(), ctx);

        Assert.Equal(45.00m, Amount(r, WithholdingTaxType.Irrf));    // 1.5%
        Assert.Equal(19.50m, Amount(r, WithholdingTaxType.Pis));     // 0.65%
        Assert.Equal(90.00m, Amount(r, WithholdingTaxType.Cofins));  // 3%
        Assert.Equal(30.00m, Amount(r, WithholdingTaxType.Csll));    // 1%
        Assert.Equal(0m, Amount(r, WithholdingTaxType.Inss));        // not labor-supply (no 11.02)
        Assert.Equal(0m, Amount(r, WithholdingTaxType.Iss));         // no municipality → no ISS
    }
}
