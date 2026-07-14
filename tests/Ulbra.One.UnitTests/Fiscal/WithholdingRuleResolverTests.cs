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
        // INSS (previdenciária) keyed by the vendor tax regime — NOT the LC116 subitem: any service withholds it.
        // Geral 11%; CPRB 3,5%; Simples Anexo IV 11%; demais anexos do Simples isento.
        new(WithholdingTaxType.Inss, retains: true, RateSource.Fixed, 0.11m),
        new(WithholdingTaxType.Inss, retains: true, RateSource.Fixed, 0.035m, cprb: true),
        new(WithholdingTaxType.Inss, retains: false, RateSource.Fixed, null, vendorRegime: TaxRegime.SimplesNacional, reason: NonRetentionReason.RegimeExemption),
        new(WithholdingTaxType.Inss, retains: true, RateSource.Fixed, 0.11m, vendorRegime: TaxRegime.SimplesNacional, simplesAnexo: SimplesAnexo.IV),
        // ISS: non-Simples uses the municipal rate (Canoas 5%); Simples uses the invoice rate (band).
        new(WithholdingTaxType.Iss, retains: true, RateSource.Fixed, 0.05m, municipalityIbge: "4304606"),
        new(WithholdingTaxType.Iss, retains: true, RateSource.FromNota, null, vendorRegime: TaxRegime.SimplesNacional),
    ];

    private static decimal Amount(IReadOnlyList<WithholdingComputation> r, WithholdingTaxType t) =>
        r.Where(c => c.TaxType == t).Sum(c => c.Amount);

    [Fact]
    public void LucroPresumido_service_withholds_irrf_csrf_iss_and_inss()
    {
        var ctx = new ResolutionContext(null, TaxRegime.LucroPresumido,
            [new ServiceLine("17.01", "4304606", null, 3000m)]);

        var r = WithholdingRuleResolver.Resolve(BaseRules(), ctx);

        Assert.Equal(45.00m, Amount(r, WithholdingTaxType.Irrf));    // 1.5%
        Assert.Equal(19.50m, Amount(r, WithholdingTaxType.Pis));     // 0.65%
        Assert.Equal(90.00m, Amount(r, WithholdingTaxType.Cofins));  // 3%
        Assert.Equal(30.00m, Amount(r, WithholdingTaxType.Csll));    // 1%
        Assert.Equal(150.00m, Amount(r, WithholdingTaxType.Iss));    // 5% (Canoas)
        Assert.Equal(330.00m, Amount(r, WithholdingTaxType.Inss));   // 11% — INSS pelo regime (PJ normal), independe do subitem
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
        Assert.Equal(0m, Amount(r, WithholdingTaxType.Inss));        // Simples sem Anexo IV → isento
        Assert.Equal(90.00m, Amount(r, WithholdingTaxType.Iss));     // 3% from the invoice
    }

    [Fact]
    public void Inss_normal_pj_withholds_11pct_regardless_of_subitem()
    {
        // A retenção de INSS segue o regime do Fornecedor, não o subitem LC116: um serviço sem subitem
        // (NFS-e sem o campo, ou lançamento manual) ainda retém.
        var ctx = new ResolutionContext(null, TaxRegime.LucroReal,
            [new ServiceLine(null, "4304606", null, 5000m)]);

        var r = WithholdingRuleResolver.Resolve(BaseRules(), ctx);

        Assert.Equal(550.00m, Amount(r, WithholdingTaxType.Inss));   // 11%
    }

    [Fact]
    public void Inss_cprb_provider_withholds_3_5pct()
    {
        // CPRB destacado na NF (confirmado no lançamento) → 3,5% em vez de 11%.
        var ctx = new ResolutionContext(null, TaxRegime.LucroPresumido,
            [new ServiceLine(null, "4304606", null, 5000m)], Cprb: true);

        var r = WithholdingRuleResolver.Resolve(BaseRules(), ctx);

        Assert.Equal(175.00m, Amount(r, WithholdingTaxType.Inss));   // 3,5%
    }

    [Fact]
    public void Inss_simples_anexo_iv_withholds_11pct()
    {
        var ctx = new ResolutionContext(null, TaxRegime.SimplesNacional,
            [new ServiceLine(null, "4304606", null, 5000m)], SimplesAnexo: SimplesAnexo.IV);

        var r = WithholdingRuleResolver.Resolve(BaseRules(), ctx);

        Assert.Equal(550.00m, Amount(r, WithholdingTaxType.Inss));   // 11% (Anexo IV recolhe CPP fora do DAS)
    }

    [Theory]
    [InlineData(SimplesAnexo.III)]
    [InlineData(null)] // anexo não informado → isento por padrão
    public void Inss_simples_non_iv_or_unknown_is_exempt(SimplesAnexo? anexo)
    {
        var ctx = new ResolutionContext(null, TaxRegime.SimplesNacional,
            [new ServiceLine(null, "4304606", null, 5000m)], SimplesAnexo: anexo);

        var r = WithholdingRuleResolver.Resolve(BaseRules(), ctx);

        Assert.Equal(0m, Amount(r, WithholdingTaxType.Inss));        // isento
    }

    [Fact]
    public void Inss_cprb_does_not_override_simples_exemption()
    {
        // Edge: CPRB é regime não-Simples — um Simples marcado como CPRB continua governado pelo anexo (III = isento).
        var ctx = new ResolutionContext(null, TaxRegime.SimplesNacional,
            [new ServiceLine(null, "4304606", null, 5000m)], Cprb: true, SimplesAnexo: SimplesAnexo.III);

        var r = WithholdingRuleResolver.Resolve(BaseRules(), ctx);

        Assert.Equal(0m, Amount(r, WithholdingTaxType.Inss));        // isento (não 3,5%)
    }

    [Fact]
    public void Inss_cprb_false_confirmed_keeps_11pct()
    {
        // NF confirma que NÃO há CPRB destacado → volta aos 11% (a confirmação sobrepõe a sugestão do cadastro).
        var ctx = new ResolutionContext(null, TaxRegime.LucroReal,
            [new ServiceLine(null, "4304606", null, 5000m)], Cprb: false);

        var r = WithholdingRuleResolver.Resolve(BaseRules(), ctx);

        Assert.Equal(550.00m, Amount(r, WithholdingTaxType.Inss));   // 11%
    }

    [Fact]
    public void Inss_applies_even_for_non_cessao_subitem()
    {
        // A retenção de INSS segue o regime, não o subitem: um serviço fora da "cessão de mão de obra"
        // (ex.: 17.01) ainda retém INSS pelo regime do Fornecedor.
        var ctx = new ResolutionContext(null, TaxRegime.LucroPresumido,
            [new ServiceLine("17.01", "4304606", null, 5000m)]);

        var r = WithholdingRuleResolver.Resolve(BaseRules(), ctx);

        Assert.Equal(550.00m, Amount(r, WithholdingTaxType.Inss));   // 11% mesmo em 17.01
    }

    [Fact]
    public void Service_without_lc116_subitem_withholds_federal_and_inss()
    {
        // The LC116 subitem is a refinement, not the trigger: a service whose subitem wasn't parsed still
        // withholds the general federal taxes AND the INSS (which now follows the vendor regime). Only ISS,
        // which needs the municipality, stays off.
        var ctx = new ResolutionContext(null, TaxRegime.LucroPresumido,
            [new ServiceLine(null, null, null, 3000m)]);

        var r = WithholdingRuleResolver.Resolve(BaseRules(), ctx);

        Assert.Equal(45.00m, Amount(r, WithholdingTaxType.Irrf));    // 1.5%
        Assert.Equal(19.50m, Amount(r, WithholdingTaxType.Pis));     // 0.65%
        Assert.Equal(90.00m, Amount(r, WithholdingTaxType.Cofins));  // 3%
        Assert.Equal(30.00m, Amount(r, WithholdingTaxType.Csll));    // 1%
        Assert.Equal(330.00m, Amount(r, WithholdingTaxType.Inss));   // 11% — INSS pelo regime, mesmo sem subitem
        Assert.Equal(0m, Amount(r, WithholdingTaxType.Iss));         // no municipality → no ISS
    }

    // ---- Vendor-specific rules (retenção por Fornecedor) ----

    private static readonly Guid Vendor = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherVendor = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Vendor_rule_beats_a_more_specific_system_rule()
    {
        // A vendor-only rule (DimensionCount 0) must beat the system IRRF rule even though the latter could be
        // more specific — vendor priority is the PRIMARY sort key, above DimensionCount.
        var rules = BaseRules();
        rules.Add(new WithholdingRule(WithholdingTaxType.Irrf, retains: true, RateSource.Fixed, 0.05m, vendorId: Vendor));

        var ctx = new ResolutionContext(null, TaxRegime.LucroPresumido,
            [new ServiceLine(null, null, null, 1000m)], VendorId: Vendor);

        var r = WithholdingRuleResolver.Resolve(rules, ctx);

        Assert.Equal(50.00m, Amount(r, WithholdingTaxType.Irrf));    // 5% do fornecedor, não 1,5% do sistema
    }

    [Fact]
    public void Vendor_rule_only_overrides_its_own_tax_others_fall_back_to_system()
    {
        // The vendor pins only IRRF; every other tax must resolve from the system defaults for that same vendor.
        var rules = BaseRules();
        rules.Add(new WithholdingRule(WithholdingTaxType.Irrf, retains: true, RateSource.Fixed, 0.05m, vendorId: Vendor));

        var ctx = new ResolutionContext(null, TaxRegime.LucroPresumido,
            [new ServiceLine(null, null, null, 1000m)], VendorId: Vendor);

        var r = WithholdingRuleResolver.Resolve(rules, ctx);

        Assert.Equal(50.00m, Amount(r, WithholdingTaxType.Irrf));    // 5% (fornecedor)
        Assert.Equal(6.50m, Amount(r, WithholdingTaxType.Pis));      // 0,65% (sistema)
        Assert.Equal(30.00m, Amount(r, WithholdingTaxType.Cofins));  // 3% (sistema)
        Assert.Equal(10.00m, Amount(r, WithholdingTaxType.Csll));    // 1% (sistema)
        Assert.Equal(110.00m, Amount(r, WithholdingTaxType.Inss));   // 11% (sistema)
    }

    [Fact]
    public void Vendor_rule_does_not_apply_to_a_different_vendor()
    {
        var rules = BaseRules();
        rules.Add(new WithholdingRule(WithholdingTaxType.Irrf, retains: true, RateSource.Fixed, 0.05m, vendorId: Vendor));

        var ctx = new ResolutionContext(null, TaxRegime.LucroPresumido,
            [new ServiceLine(null, null, null, 1000m)], VendorId: OtherVendor);

        var r = WithholdingRuleResolver.Resolve(rules, ctx);

        Assert.Equal(15.00m, Amount(r, WithholdingTaxType.Irrf));    // 1,5% do sistema (a regra do Vendor não casa)
    }

    [Fact]
    public void Vendor_does_not_retain_override_zeroes_a_system_withholding()
    {
        // A vendor rule can EXEMPT a tax the system would normally withhold.
        var rules = BaseRules();
        rules.Add(new WithholdingRule(WithholdingTaxType.Inss, retains: false, RateSource.Fixed, null,
            vendorId: Vendor, reason: NonRetentionReason.RegimeExemption));

        var ctx = new ResolutionContext(null, TaxRegime.LucroReal,
            [new ServiceLine(null, "4304606", null, 5000m)], VendorId: Vendor);

        var r = WithholdingRuleResolver.Resolve(rules, ctx);

        Assert.Equal(0m, Amount(r, WithholdingTaxType.Inss));        // fornecedor zera o INSS que o sistema reteria
    }

    [Fact]
    public void Vendor_rule_can_reenable_a_tax_the_simples_exemption_turns_off()
    {
        // A Simples vendor is exempt from IRRF by the system rule; a vendor override re-enables it (wins by priority).
        var rules = BaseRules();
        rules.Add(new WithholdingRule(WithholdingTaxType.Irrf, retains: true, RateSource.Fixed, 0.015m, vendorId: Vendor));

        var ctx = new ResolutionContext(null, TaxRegime.SimplesNacional,
            [new ServiceLine(null, null, null, 1000m)], VendorId: Vendor);

        var r = WithholdingRuleResolver.Resolve(rules, ctx);

        Assert.Equal(15.00m, Amount(r, WithholdingTaxType.Irrf));    // 1,5% — sobrepõe a isenção do Simples
    }

    [Fact]
    public void Within_vendor_the_most_specific_rule_wins()
    {
        // Two vendor rules for INSS: a vendor-only one and a vendor+CPRB one. With CPRB confirmed, the pinned
        // (higher DimensionCount) vendor rule wins — specificity still applies inside the vendor tier.
        var rules = BaseRules();
        rules.Add(new WithholdingRule(WithholdingTaxType.Inss, retains: true, RateSource.Fixed, 0.11m, vendorId: Vendor));
        rules.Add(new WithholdingRule(WithholdingTaxType.Inss, retains: true, RateSource.Fixed, 0.035m, vendorId: Vendor, cprb: true));

        var ctx = new ResolutionContext(null, TaxRegime.LucroPresumido,
            [new ServiceLine(null, null, null, 1000m)], Cprb: true, VendorId: Vendor);

        var r = WithholdingRuleResolver.Resolve(rules, ctx);

        Assert.Equal(35.00m, Amount(r, WithholdingTaxType.Inss));    // 3,5% (vendor+CPRB) vence vendor-only 11%
    }

    [Fact]
    public void ResolvePerTax_previews_system_rules_per_tax()
    {
        // Preview uses only system rules (no vendorId in context): Simples → federal não retém; INSS Anexo IV retém 11%.
        var ctx = new ResolutionContext(null, TaxRegime.SimplesNacional,
            [new ServiceLine(null, null, null, 100m)], SimplesAnexo: SimplesAnexo.IV);

        var perTax = WithholdingRuleResolver.ResolvePerTax(BaseRules(), ctx);

        var irrf = perTax.Single(x => x.Tax == WithholdingTaxType.Irrf).Rule;
        Assert.NotNull(irrf);
        Assert.False(irrf!.Retains);                                 // isenção do Simples

        var inss = perTax.Single(x => x.Tax == WithholdingTaxType.Inss).Rule;
        Assert.NotNull(inss);
        Assert.True(inss!.Retains);
        Assert.Equal(0.11m, inss.Rate);                             // Anexo IV → 11%
    }
}
