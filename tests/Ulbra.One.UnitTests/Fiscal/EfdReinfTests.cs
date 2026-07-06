using System.Xml;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Fiscal.Domain;
using Ulbra.One.Fiscal.Domain.Assessment;
using Ulbra.One.Fiscal.Domain.EfdReinf;
using Xunit;

namespace Ulbra.One.UnitTests.Fiscal;

public class ReinfEventMapperTests
{
    [Theory]
    [InlineData(RetentionGroup.Inss, ReinfEvent.R2010)]
    [InlineData(RetentionGroup.Irrf, ReinfEvent.R4020)]
    [InlineData(RetentionGroup.Csrf, ReinfEvent.R4020)]
    [InlineData(RetentionGroup.Iss, ReinfEvent.Unmapped)]
    [InlineData(RetentionGroup.Other, ReinfEvent.Unmapped)]
    public void Maps_group_to_event(RetentionGroup group, ReinfEvent expected) =>
        Assert.Equal(expected, ReinfEventMapper.Map(group));
}

public class IncomeNatureResolverTests
{
    [Fact]
    public void Known_lc116_subitem_maps_to_its_natRend() =>
        Assert.Equal("10002", IncomeNatureResolver.Resolve("17.01", RetentionGroup.Irrf));

    [Fact]
    public void Null_or_unknown_subitem_falls_back_to_default()
    {
        Assert.Equal("15009", IncomeNatureResolver.Resolve(null, RetentionGroup.Csrf));
        Assert.Equal("15009", IncomeNatureResolver.Resolve("99.99", RetentionGroup.Csrf));
    }
}

public class ReinfR4020ProjectionTests
{
    private static WithholdingFact Fact(Guid payableId, string estabCnpj, string benef, WithholdingTaxType tax,
        decimal baseAmount, decimal amount, string? lc116 = null) =>
        new(payableId, "Forn", "NF", Guid.NewGuid(), "Empresa", estabCnpj, Guid.NewGuid(), "CC", "Centro",
            Guid.NewGuid(), benef, tax, baseAmount, amount, new DateOnly(2026, 7, 3), lc116);

    [Fact]
    public void Includes_only_r4020_guias_and_sums_per_tax()
    {
        var p = Guid.NewGuid();
        var items = ReinfR4020Projection.FromFacts(
        [
            Fact(p, "99999999000191", "11222333000181", WithholdingTaxType.Irrf, 1000m, 15m),
            Fact(p, "99999999000191", "11222333000181", WithholdingTaxType.Pis, 1000m, 6.5m),
            Fact(p, "99999999000191", "11222333000181", WithholdingTaxType.Cofins, 1000m, 30m),
            Fact(p, "99999999000191", "11222333000181", WithholdingTaxType.Csll, 1000m, 10m),
            Fact(p, "99999999000191", "11222333000181", WithholdingTaxType.Inss, 1000m, 110m), // fora (R-2010)
            Fact(p, "99999999000191", "11222333000181", WithholdingTaxType.Iss, 1000m, 50m),   // fora (municipal)
        ]);

        var item = Assert.Single(items);
        Assert.Equal(1000m, item.BaseAmount);
        Assert.Equal(15m, item.Irrf);
        Assert.Equal(6.5m, item.Pis);
        Assert.Equal(30m, item.Cofins);
        Assert.Equal(10m, item.Csll);
    }

    [Fact]
    public void Base_sums_across_distinct_payables_not_per_tax_line()
    {
        var items = ReinfR4020Projection.FromFacts(
        [
            Fact(Guid.NewGuid(), "E", "B", WithholdingTaxType.Irrf, 1000m, 15m),
            Fact(Guid.NewGuid(), "E", "B", WithholdingTaxType.Irrf, 2000m, 30m),
        ]);
        var item = Assert.Single(items);
        Assert.Equal(3000m, item.BaseAmount);
        Assert.Equal(45m, item.Irrf);
    }

    [Fact]
    public void Groups_by_beneficiary()
    {
        var items = ReinfR4020Projection.FromFacts(
        [
            Fact(Guid.NewGuid(), "E", "B1", WithholdingTaxType.Irrf, 1000m, 15m),
            Fact(Guid.NewGuid(), "E", "B2", WithholdingTaxType.Irrf, 1000m, 15m),
        ]);
        Assert.Equal(2, items.Count);
    }
}

public class EfdReinfXmlBuilderTests
{
    private static ReinfR4020Input Input() => new(
        "2026-07", 2, new ReinfContribuinte("99999999", "Empresa A"),
        [new ReinfR4020Item("99999999000191", "11222333000181", "15009", 1000m, 15m, 6.5m, 30m, 10m)]);

    [Fact]
    public void Produces_well_formed_xml()
    {
        var xml = EfdReinfXmlBuilder.Build(Input());
        var doc = new XmlDocument();
        doc.LoadXml(xml); // lança se malformado
        Assert.Contains("evtRetPJ", xml);
    }

    [Fact]
    public void Carries_contribuinte_estab_beneficiary_natRend_and_ambiente()
    {
        var xml = EfdReinfXmlBuilder.Build(Input());
        Assert.Contains("<nrInscr>99999999</nrInscr>", xml);       // ideContri raiz(8)
        Assert.Contains("99999999000191", xml);                    // ideEstab CNPJ(14)
        Assert.Contains("11222333000181", xml);                    // beneficiário
        Assert.Contains("<natRend>15009</natRend>", xml);
        Assert.Contains("<tpAmb>2</tpAmb>", xml);
    }
}
