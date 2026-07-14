using Ulbra.One.Finance.Domain;
using Ulbra.One.Fiscal.Domain;
using Ulbra.One.Fiscal.Domain.Entities;
using Ulbra.One.Registry.Domain.Entities;
using Xunit;

namespace Ulbra.One.UnitTests.Fiscal;

/// <summary>Domain guards for the WithholdingRule CRUD dimensions. The INSS-specific Cprb / SimplesAnexo must
/// survive BOTH the constructor and <c>Update</c> — the CRUD path (endpoint → Update) previously dropped them,
/// so a rule edited via the API silently lost its CPRB / Anexo pinning.</summary>
public class WithholdingRuleTests
{
    [Fact]
    public void Constructor_carries_cprb_and_simples_anexo()
    {
        var rule = new WithholdingRule(WithholdingTaxType.Inss, retains: true, RateSource.Fixed, 0.035m,
            cprb: true, simplesAnexo: SimplesAnexo.IV);

        Assert.True(rule.Cprb);
        Assert.Equal(SimplesAnexo.IV, rule.SimplesAnexo);
    }

    [Fact]
    public void Update_carries_cprb_and_simples_anexo()
    {
        var rule = new WithholdingRule(WithholdingTaxType.Inss, retains: true, RateSource.Fixed, 0.11m);
        Assert.Null(rule.Cprb);
        Assert.Null(rule.SimplesAnexo);

        rule.Update(WithholdingTaxType.Inss, retains: true, RateSource.Fixed, 0.035m,
            lc116Subitem: null, vendorRegime: TaxRegime.SimplesNacional, municipalityIbge: null, companyId: null,
            baseRule: BaseRule.Full, minimumAmount: null, reason: NonRetentionReason.None,
            cprb: true, simplesAnexo: SimplesAnexo.IV);

        Assert.True(rule.Cprb);
        Assert.Equal(SimplesAnexo.IV, rule.SimplesAnexo);
        Assert.Equal(0.035m, rule.Rate);
        Assert.Equal(TaxRegime.SimplesNacional, rule.VendorRegime);
    }

    [Fact]
    public void Update_can_clear_cprb_and_simples_anexo()
    {
        var rule = new WithholdingRule(WithholdingTaxType.Inss, retains: true, RateSource.Fixed, 0.035m,
            cprb: true, simplesAnexo: SimplesAnexo.IV);

        rule.Update(WithholdingTaxType.Inss, retains: true, RateSource.Fixed, 0.11m,
            lc116Subitem: null, vendorRegime: null, municipalityIbge: null, companyId: null,
            baseRule: BaseRule.Full, minimumAmount: null, reason: NonRetentionReason.None,
            cprb: null, simplesAnexo: null);

        Assert.Null(rule.Cprb);
        Assert.Null(rule.SimplesAnexo);
        Assert.Equal(0, rule.DimensionCount);
    }

    [Fact]
    public void DimensionCount_counts_cprb_and_simples_anexo()
    {
        var geral = new WithholdingRule(WithholdingTaxType.Inss, retains: true, RateSource.Fixed, 0.11m);
        Assert.Equal(0, geral.DimensionCount);

        var cprb = new WithholdingRule(WithholdingTaxType.Inss, retains: true, RateSource.Fixed, 0.035m, cprb: true);
        Assert.Equal(1, cprb.DimensionCount);

        var anexoIv = new WithholdingRule(WithholdingTaxType.Inss, retains: true, RateSource.Fixed, 0.11m,
            vendorRegime: TaxRegime.SimplesNacional, simplesAnexo: SimplesAnexo.IV);
        Assert.Equal(2, anexoIv.DimensionCount); // vendorRegime + simplesAnexo
    }

    [Fact]
    public void Constructor_and_update_carry_vendor_id()
    {
        var vendorId = Guid.NewGuid();
        var rule = new WithholdingRule(WithholdingTaxType.Irrf, retains: true, RateSource.Fixed, 0.05m,
            vendorId: vendorId);
        Assert.Equal(vendorId, rule.VendorId);

        rule.Update(WithholdingTaxType.Irrf, retains: true, RateSource.Fixed, 0.03m,
            lc116Subitem: null, vendorRegime: null, municipalityIbge: null, companyId: null,
            baseRule: BaseRule.Full, minimumAmount: null, reason: NonRetentionReason.None,
            cprb: null, simplesAnexo: null, vendorId: vendorId);
        Assert.Equal(vendorId, rule.VendorId);

        // Update can clear it (back to a system rule).
        rule.Update(WithholdingTaxType.Irrf, retains: true, RateSource.Fixed, 0.03m,
            lc116Subitem: null, vendorRegime: null, municipalityIbge: null, companyId: null,
            baseRule: BaseRule.Full, minimumAmount: null, reason: NonRetentionReason.None,
            cprb: null, simplesAnexo: null, vendorId: null);
        Assert.Null(rule.VendorId);
    }

    [Fact]
    public void DimensionCount_ignores_vendor_id()
    {
        // Vendor priority lives in the resolver's primary sort key, NOT in DimensionCount.
        var vendorOnly = new WithholdingRule(WithholdingTaxType.Irrf, retains: true, RateSource.Fixed, 0.05m,
            vendorId: Guid.NewGuid());
        Assert.Equal(0, vendorOnly.DimensionCount);
    }
}
