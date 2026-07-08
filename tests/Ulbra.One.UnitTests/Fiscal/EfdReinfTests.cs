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
        decimal baseAmount, decimal amount, string? lc116 = null, decimal gross = 1000m) =>
        new(payableId, "Forn", "NF", Guid.NewGuid(), "Empresa", estabCnpj, Guid.NewGuid(), "CC", "Centro",
            Guid.NewGuid(), benef, tax, baseAmount, amount, gross, new DateOnly(2026, 7, 3), lc116);

    [Fact]
    public void Includes_only_r4020_guias_and_sums_base_and_withheld_per_tax()
    {
        var p = Guid.NewGuid();
        var payments = ReinfR4020Projection.FromFacts(
        [
            Fact(p, "99999999000191", "11222333000181", WithholdingTaxType.Irrf, 1000m, 15m, gross: 1000m),
            Fact(p, "99999999000191", "11222333000181", WithholdingTaxType.Pis, 1000m, 6.5m, gross: 1000m),
            Fact(p, "99999999000191", "11222333000181", WithholdingTaxType.Cofins, 1000m, 30m, gross: 1000m),
            Fact(p, "99999999000191", "11222333000181", WithholdingTaxType.Csll, 1000m, 10m, gross: 1000m),
            Fact(p, "99999999000191", "11222333000181", WithholdingTaxType.Inss, 1000m, 110m), // fora (R-2010)
            Fact(p, "99999999000191", "11222333000181", WithholdingTaxType.Iss, 1000m, 50m),   // fora (municipal)
        ]);

        var pay = Assert.Single(payments);
        Assert.Equal(1000m, pay.Gross);
        Assert.Equal(1000m, pay.BaseIrrf);
        Assert.Equal(15m, pay.Irrf);
        Assert.Equal(6.5m, pay.Pis);
        Assert.Equal(30m, pay.Cofins);
        Assert.Equal(10m, pay.Csll);
    }

    [Fact]
    public void Produces_one_payment_per_payable() // infoPgto é por título (fato gerador)
    {
        var payments = ReinfR4020Projection.FromFacts(
        [
            Fact(Guid.NewGuid(), "E", "B", WithholdingTaxType.Irrf, 1000m, 15m),
            Fact(Guid.NewGuid(), "E", "B", WithholdingTaxType.Irrf, 2000m, 30m),
        ]);
        Assert.Equal(2, payments.Count);
        Assert.Equal([1000m, 2000m], payments.Select(x => x.BaseIrrf).OrderBy(x => x));
        Assert.Equal([15m, 30m], payments.Select(x => x.Irrf).OrderBy(x => x));
    }

    [Fact]
    public void Groups_by_beneficiary()
    {
        var payments = ReinfR4020Projection.FromFacts(
        [
            Fact(Guid.NewGuid(), "E", "B1", WithholdingTaxType.Irrf, 1000m, 15m),
            Fact(Guid.NewGuid(), "E", "B2", WithholdingTaxType.Irrf, 1000m, 15m),
        ]);
        Assert.Equal(2, payments.Count);
    }

    [Fact]
    public void Strips_cnpj_mask_from_estab_and_beneficiary() // Company.Cnpj may carry a mask; XSD wants digits
    {
        var payments = ReinfR4020Projection.FromFacts(
        [
            Fact(Guid.NewGuid(), "88.332.466/0001-03", "11.222.333/0001-81", WithholdingTaxType.Irrf, 1000m, 15m),
        ]);
        var pay = Assert.Single(payments);
        Assert.Equal("88332466000103", pay.EstabCnpj);
        Assert.Equal("11222333000181", pay.BeneficiaryCnpj);
    }
}

public class EfdReinfXmlBuilderTests
{
    private static ReinfR4020Input Input() => new(
        "2026-07", 2, new ReinfTaxpayer("99999999", "Empresa A"),
        [new ReinfR4020Payment("99999999000191", "11222333000181", "15009",
            new DateOnly(2026, 7, 15), 1000m, 1000m, 15m, 1000m, 10m, 1000m, 30m, 1000m, 6.5m)]);

    [Fact]
    public void Produces_well_formed_xml()
    {
        var xml = EfdReinfXmlBuilder.Build(Input());
        var doc = new XmlDocument();
        doc.LoadXml(xml); // lança se malformado
        Assert.Contains("evtRetPJ", xml);
    }

    [Fact]
    public void Uses_the_official_event_schema_and_corrected_tags()
    {
        var xml = EfdReinfXmlBuilder.Build(Input());
        Assert.Contains("evt4020PagtoBeneficiarioPJ/v2_01_02", xml);   // namespace do evento correto
        Assert.Contains("<nrInsc>99999999</nrInsc>", xml);             // ideContri (tpInsc/nrInsc, sem "r")
        Assert.Contains("<nrInscEstab>99999999000191</nrInscEstab>", xml);
        Assert.Contains("<cnpjBenef>11222333000181</cnpjBenef>", xml);
        Assert.Contains("<natRend>15009</natRend>", xml);
        Assert.Contains("<tpAmb>2</tpAmb>", xml);
        Assert.Contains("<vlrBruto>1000,00</vlrBruto>", xml);          // vírgula decimal
        Assert.Contains("<vlrPP>6,50</vlrPP>", xml);                   // PIS/PASEP (não vlrPisPasep)
        Assert.Contains("<vlrCSLL>10,00</vlrCSLL>", xml);              // caixa correta
        Assert.DoesNotContain("vlrBaseCSRF", xml);                     // CSRF não existe no leiaute
        Assert.DoesNotContain("vlrPisPasep", xml);
    }
}
