using Ulbra.One.Finance.Domain;
using Ulbra.One.Fiscal.Domain;
using Ulbra.One.Fiscal.Domain.Entities;
using Ulbra.One.Fiscal.Domain.Withholdings;
using Ulbra.One.Registry.Domain.Entities;
using Xunit;

namespace Ulbra.One.UnitTests.Fiscal;

/// <summary>Tabela de aceitação do cálculo automático de retenção (HTML §4.5).</summary>
public class WithholdingRuleResolverTests
{
    // Conjunto-base de regras (espelha o seed que a Contabilidade valida).
    private static List<WithholdingRule> BaseRules() =>
    [
        // Federais gerais (qualquer regime).
        new(WithholdingTaxType.Irrf, retains: true, RateSource.Fixed, 0.015m),
        new(WithholdingTaxType.Pis, retains: true, RateSource.Fixed, 0.0065m),
        new(WithholdingTaxType.Cofins, retains: true, RateSource.Fixed, 0.03m),
        new(WithholdingTaxType.Csll, retains: true, RateSource.Fixed, 0.01m),
        // Dispensa do Simples (federais de renda/contribuição) — condicionada à declaração.
        new(WithholdingTaxType.Irrf, retains: false, RateSource.Fixed, null, vendorRegime: TaxRegime.SimplesNacional, reason: NonRetentionReason.RegimeDispensa),
        new(WithholdingTaxType.Pis, retains: false, RateSource.Fixed, null, vendorRegime: TaxRegime.SimplesNacional, reason: NonRetentionReason.RegimeDispensa),
        new(WithholdingTaxType.Cofins, retains: false, RateSource.Fixed, null, vendorRegime: TaxRegime.SimplesNacional, reason: NonRetentionReason.RegimeDispensa),
        new(WithholdingTaxType.Csll, retains: false, RateSource.Fixed, null, vendorRegime: TaxRegime.SimplesNacional, reason: NonRetentionReason.RegimeDispensa),
        // INSS 11% para cessão de mão de obra (subitem 11.02 = vigilância) — qualquer regime.
        new(WithholdingTaxType.Inss, retains: true, RateSource.Fixed, 0.11m, lc116Subitem: "11.02"),
        // ISS: não-Simples usa a alíquota do município (Canoas 5%); Simples usa a alíquota da nota (faixa).
        new(WithholdingTaxType.Iss, retains: true, RateSource.Fixed, 0.05m, municipalityIbge: "4304606"),
        new(WithholdingTaxType.Iss, retains: true, RateSource.FromNota, null, vendorRegime: TaxRegime.SimplesNacional),
    ];

    private static decimal Amount(IReadOnlyList<WithholdingComputation> r, WithholdingTaxType t) =>
        r.Where(c => c.TaxType == t).Sum(c => c.Amount);

    [Fact]
    public void LucroPresumido_assessoria_em_canoas_retem_irrf_csrf_e_iss()
    {
        var ctx = new ResolutionContext(null, TaxRegime.LucroPresumido, false,
            [new ServiceLine("17.01", "4304606", null, 3000m)]);

        var r = WithholdingRuleResolver.Resolve(BaseRules(), ctx);

        Assert.Equal(45.00m, Amount(r, WithholdingTaxType.Irrf));    // 1,5%
        Assert.Equal(19.50m, Amount(r, WithholdingTaxType.Pis));     // 0,65%
        Assert.Equal(90.00m, Amount(r, WithholdingTaxType.Cofins));  // 3%
        Assert.Equal(30.00m, Amount(r, WithholdingTaxType.Csll));    // 1%
        Assert.Equal(150.00m, Amount(r, WithholdingTaxType.Iss));    // 5% (Canoas)
        Assert.Equal(0m, Amount(r, WithholdingTaxType.Inss));        // não é cessão de MO
    }

    [Fact]
    public void Simples_com_declaracao_nao_retem_federal_mas_retem_iss_da_nota()
    {
        var ctx = new ResolutionContext(null, TaxRegime.SimplesNacional, SimplesDeclarationProvided: true,
            [new ServiceLine("17.01", "4304606", 0.03m, 3000m)]); // nota traz ISS 3% (faixa do Simples)

        var r = WithholdingRuleResolver.Resolve(BaseRules(), ctx);

        Assert.Equal(0m, Amount(r, WithholdingTaxType.Irrf));
        Assert.Equal(0m, Amount(r, WithholdingTaxType.Pis));
        Assert.Equal(0m, Amount(r, WithholdingTaxType.Cofins));
        Assert.Equal(0m, Amount(r, WithholdingTaxType.Csll));
        Assert.Equal(90.00m, Amount(r, WithholdingTaxType.Iss));     // 3% da nota
    }

    [Fact]
    public void Simples_sem_declaracao_retem_federal()
    {
        var ctx = new ResolutionContext(null, TaxRegime.SimplesNacional, SimplesDeclarationProvided: false,
            [new ServiceLine("17.01", "4304606", 0.03m, 3000m)]);

        var r = WithholdingRuleResolver.Resolve(BaseRules(), ctx);

        Assert.Equal(45.00m, Amount(r, WithholdingTaxType.Irrf));    // dispensa cai → retém
        Assert.Equal(19.50m, Amount(r, WithholdingTaxType.Pis));
        Assert.Equal(90.00m, Amount(r, WithholdingTaxType.Cofins));
        Assert.Equal(30.00m, Amount(r, WithholdingTaxType.Csll));
    }

    [Fact]
    public void Cessao_de_mao_de_obra_retem_inss_em_qualquer_regime()
    {
        var ctx = new ResolutionContext(null, TaxRegime.SimplesNacional, SimplesDeclarationProvided: true,
            [new ServiceLine("11.02", "4304606", null, 5000m)]); // vigilância

        var r = WithholdingRuleResolver.Resolve(BaseRules(), ctx);

        Assert.Equal(550.00m, Amount(r, WithholdingTaxType.Inss));   // 11% mesmo no Simples
    }

    [Fact]
    public void Produto_sem_linha_de_servico_nao_gera_retencao()
    {
        var ctx = new ResolutionContext(null, TaxRegime.SimplesNacional, false,
            [new ServiceLine(null, null, null, 15500m)]); // NF-e produto: sem subitem LC116

        var r = WithholdingRuleResolver.Resolve(BaseRules(), ctx);

        Assert.Empty(r);
    }
}
