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
        // INSS on labor-supply (LC116 11.02): geral 11%; CPRB 3,5%; Simples Anexo IV 11%; demais anexos isento.
        new(WithholdingTaxType.Inss, retains: true, RateSource.Fixed, 0.11m, lc116Subitem: "11.02"),
        new(WithholdingTaxType.Inss, retains: true, RateSource.Fixed, 0.035m, lc116Subitem: "11.02", cprb: true),
        new(WithholdingTaxType.Inss, retains: false, RateSource.Fixed, null, lc116Subitem: "11.02", vendorRegime: TaxRegime.SimplesNacional, reason: NonRetentionReason.RegimeExemption),
        new(WithholdingTaxType.Inss, retains: true, RateSource.Fixed, 0.11m, lc116Subitem: "11.02", vendorRegime: TaxRegime.SimplesNacional, simplesAnexo: SimplesAnexo.IV),
        // ISS: non-Simples uses the municipal rate (Canoas 5%); Simples uses the invoice rate (band).
        new(WithholdingTaxType.Iss, retains: true, RateSource.Fixed, 0.05m, municipalityIbge: "4304606"),
        new(WithholdingTaxType.Iss, retains: true, RateSource.FromNota, null, vendorRegime: TaxRegime.SimplesNacional),
    ];

    private static decimal Amount(IReadOnlyList<WithholdingComputation> r, WithholdingTaxType t) =>
        r.Where(c => c.TaxType == t).Sum(c => c.Amount);

    [Fact]
    public void LucroPresumido_service_withholds_irrf_csrf_and_iss()
    {
        var ctx = new ResolutionContext(null, TaxRegime.LucroPresumido,
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
    public void Simples_service_skips_federal_keeps_iss_from_nota()
    {
        // Regime alone decides: a SimplesNacional vendor is exempt from the federal withholding (the exemption
        // rule wins by specificity) and the ISS is withheld at the band rate the invoice carries.
        var ctx = new ResolutionContext(null, TaxRegime.SimplesNacional,
            [new ServiceLine("17.01", "4304606", 0.03m, 3000m)]); // invoice carries ISS 3% (Simples band)

        var r = WithholdingRuleResolver.Resolve(BaseRules(), ctx);

        Assert.Equal(0m, Amount(r, WithholdingTaxType.Irrf));
        Assert.Equal(0m, Amount(r, WithholdingTaxType.Pis));
        Assert.Equal(0m, Amount(r, WithholdingTaxType.Cofins));
        Assert.Equal(0m, Amount(r, WithholdingTaxType.Csll));
        Assert.Equal(90.00m, Amount(r, WithholdingTaxType.Iss));     // 3% from the invoice
    }

    [Fact]
    public void Inss_labor_supply_normal_pj_withholds_11pct()
    {
        var ctx = new ResolutionContext(null, TaxRegime.LucroReal,
            [new ServiceLine("11.02", "4304606", null, 5000m)]); // vigilância

        var r = WithholdingRuleResolver.Resolve(BaseRules(), ctx);

        Assert.Equal(550.00m, Amount(r, WithholdingTaxType.Inss));   // 11%
    }

    [Fact]
    public void Inss_cprb_provider_withholds_3_5pct()
    {
        // CPRB destacado na NF (confirmado no lançamento) → 3,5% em vez de 11%.
        var ctx = new ResolutionContext(null, TaxRegime.LucroPresumido,
            [new ServiceLine("11.02", "4304606", null, 5000m)], Cprb: true);

        var r = WithholdingRuleResolver.Resolve(BaseRules(), ctx);

        Assert.Equal(175.00m, Amount(r, WithholdingTaxType.Inss));   // 3,5%
    }

    [Fact]
    public void Inss_simples_anexo_iv_withholds_11pct()
    {
        var ctx = new ResolutionContext(null, TaxRegime.SimplesNacional,
            [new ServiceLine("11.02", "4304606", null, 5000m)], SimplesAnexo: SimplesAnexo.IV);

        var r = WithholdingRuleResolver.Resolve(BaseRules(), ctx);

        Assert.Equal(550.00m, Amount(r, WithholdingTaxType.Inss));   // 11% (Anexo IV recolhe CPP fora do DAS)
    }

    [Theory]
    [InlineData(SimplesAnexo.III)]
    [InlineData(null)] // anexo não confirmado → isento por padrão (o guard de agendamento força a confirmação)
    public void Inss_simples_non_iv_or_unknown_is_exempt(SimplesAnexo? anexo)
    {
        var ctx = new ResolutionContext(null, TaxRegime.SimplesNacional,
            [new ServiceLine("11.02", "4304606", null, 5000m)], SimplesAnexo: anexo);

        var r = WithholdingRuleResolver.Resolve(BaseRules(), ctx);

        Assert.Equal(0m, Amount(r, WithholdingTaxType.Inss));        // isento
    }

    [Fact]
    public void Inss_cprb_does_not_override_simples_exemption()
    {
        // Edge: CPRB é regime não-Simples — um Simples marcado como CPRB continua governado pelo anexo (III = isento).
        var ctx = new ResolutionContext(null, TaxRegime.SimplesNacional,
            [new ServiceLine("11.02", "4304606", null, 5000m)], Cprb: true, SimplesAnexo: SimplesAnexo.III);

        var r = WithholdingRuleResolver.Resolve(BaseRules(), ctx);

        Assert.Equal(0m, Amount(r, WithholdingTaxType.Inss));        // isento (não 3,5%)
    }

    [Fact]
    public void Inss_cprb_false_confirmed_keeps_11pct()
    {
        // NF confirma que NÃO há CPRB destacado → volta aos 11% (a confirmação sobrepõe a sugestão do cadastro).
        var ctx = new ResolutionContext(null, TaxRegime.LucroReal,
            [new ServiceLine("11.02", "4304606", null, 5000m)], Cprb: false);

        var r = WithholdingRuleResolver.Resolve(BaseRules(), ctx);

        Assert.Equal(550.00m, Amount(r, WithholdingTaxType.Inss));   // 11%
    }

    [Fact]
    public void Inss_stays_off_outside_cessao_subitem_even_with_cprb()
    {
        // CPRB não cria INSS fora dos subitens de cessão/empreitada.
        var ctx = new ResolutionContext(null, TaxRegime.LucroPresumido,
            [new ServiceLine("17.01", "4304606", null, 5000m)], Cprb: true);

        var r = WithholdingRuleResolver.Resolve(BaseRules(), ctx);

        Assert.Equal(0m, Amount(r, WithholdingTaxType.Inss));        // 17.01 não é cessão → sem INSS
    }

    [Fact]
    public void Service_without_lc116_subitem_still_withholds_federal()
    {
        // The LC116 subitem is a refinement, not the trigger: a service whose subitem wasn't parsed still
        // withholds the general federal taxes. INSS (labor-supply 11.02) and ISS (needs municipality) don't apply.
        var ctx = new ResolutionContext(null, TaxRegime.LucroPresumido,
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
