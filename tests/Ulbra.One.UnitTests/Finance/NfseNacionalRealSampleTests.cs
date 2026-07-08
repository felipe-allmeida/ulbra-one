using System.Xml.Linq;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Infrastructure.FiscalDocuments;
using Ulbra.One.Fiscal.Domain;
using Ulbra.One.Fiscal.Domain.Entities;
using Ulbra.One.Fiscal.Domain.Withholdings;
using Ulbra.One.Registry.Domain.Entities;
using Xunit;
using Xunit.Abstractions;

namespace Ulbra.One.UnitTests.Finance;

/// <summary>
/// Reproduction test against a real NFS-e (national standard, SEFIN Nacional) emitted for AELBRA: prestador
/// "AVATO TECNOLOGIA S.A" (opSimpNac=1 → não optante do Simples), service "Suporte técnico em informática"
/// (cTribNac 010701 → LC 116 subitem 1.07). The two <c>Signature</c> blocks are dropped — the parser only reads
/// inside <c>infNFSe</c>/<c>infDPS</c>. Proves (a) the LC 116 subitem is surfaced in the canonical "1.07" form and
/// (b) the service carries INSS (11% for a normal-PJ vendor, driven by the vendor regime).
/// </summary>
public class NfseNacionalRealSampleTests(ITestOutputHelper output)
{
    private const string RealNfseXml = """
<?xml version="1.0" encoding="utf-8"?><NFSe versao="1.01" xmlns="http://www.sped.fazenda.gov.br/nfse"><infNFSe Id="NFS43105382237905444000186000000006006326065231381015"><xLocEmi>Itaara</xLocEmi><xLocPrestacao>Canoas</xLocPrestacao><nNFSe>60063</nNFSe><cLocIncid>4310538</cLocIncid><xLocIncid>Itaara</xLocIncid><xTribNac>Suporte técnico em informática, inclusive instalação, configuração e manutenção de programas de computação e bancos de dados.</xTribNac><xTribMun>Suporte técnico, manutenção e outros serviços em tecnologia da informa</xTribMun><xNBS>Serviços de suporte em tecnologia da informação (TI)</xNBS><verAplic>SefinNacional_1.6.0</verAplic><ambGer>2</ambGer><tpEmis>1</tpEmis><procEmi>1</procEmi><cStat>100</cStat><dhProc>2026-06-15T18:50:53-03:00</dhProc><nDFSe>5046758</nDFSe><emit><CNPJ>37905444000186</CNPJ><IM>000000000001804</IM><xNome>AVATO TECNOLOGIA S.A</xNome><enderNac><xLgr>GUILHERME KURTZ</xLgr><nro>3210</nro><xCpl>SALA 1</xCpl><xBairro>CENTRO</xBairro><cMun>4310538</cMun><UF>RS</UF><CEP>97185000</CEP></enderNac><fone>5133785980</fone></emit><valores><vBC>4402.00</vBC><pAliqAplic>2.00</pAliqAplic><vISSQN>88.04</vISSQN><vLiq>4402.00</vLiq></valores><DPS xmlns="http://www.sped.fazenda.gov.br/nfse" versao="1.01"><infDPS Id="DPS431053823790544400018600001000000008360128"><tpAmb>1</tpAmb><dhEmi>2026-06-15T18:49:34-03:00</dhEmi><verAplic>Smart DF-e 5.0.001</verAplic><serie>001</serie><nDPS>8360128</nDPS><dCompet>2026-06-15</dCompet><tpEmit>1</tpEmit><cLocEmi>4310538</cLocEmi><prest><CNPJ>37905444000186</CNPJ><IM>000000000001804</IM><fone>5133785980</fone><regTrib><opSimpNac>1</opSimpNac><regEspTrib>0</regEspTrib></regTrib></prest><toma><CNPJ>88332580000165</CNPJ><xNome>AELBRA EDUC SUPERIOR GRAD E POS</xNome><end><endNac><cMun>4304606</cMun><CEP>92120015</CEP></endNac><xLgr>FARROUPILHA</xLgr><nro>8001</nro><xBairro>CENTRO</xBairro></end><fone>34774000</fone><email>suporteti@aelbra.com.br</email></toma><serv><locPrest><cLocPrestacao>4304606</cLocPrestacao></locPrest><cServ><cTribNac>010701</cTribNac><cTribMun>001</cTribMun><xDescServ>SERVICOS DIGITAIS E SUPORTE</xDescServ><cNBS>115013000</cNBS></cServ></serv><valores><vServPrest><vServ>4402.00</vServ></vServPrest><trib><tribMun><tribISSQN>1</tribISSQN><tpRetISSQN>1</tpRetISSQN></tribMun></trib></valores></infDPS></DPS></infNFSe></NFSe>
""";

    [Fact]
    public void Real_nfse_nacional_surfaces_canonical_lc116_subitem()
    {
        var parser = new NfseNacionalXmlParser();
        var doc = XDocument.Parse(RealNfseXml);
        Assert.True(parser.CanParse(doc));

        var p = parser.Parse(doc);

        output.WriteLine($"Lc116Subitem={p.Lc116Subitem ?? "<null>"}");
        output.WriteLine($"TotalAmount={p.TotalAmount?.ToString() ?? "<null>"}");

        Assert.Equal(FiscalDocumentFormat.NfseXml, p.Format);
        Assert.Equal("60063", p.DocumentNumber);
        Assert.Equal(new DateOnly(2026, 6, 15), p.IssueDate);
        Assert.Equal(4402.00m, p.TotalAmount);
        Assert.Equal("37905444000186", p.IssuerDocument);
        Assert.Equal("AVATO TECNOLOGIA S.A", p.IssuerName);
        Assert.Equal("88332580000165", p.RecipientDocument);
        Assert.Equal("SERVICOS DIGITAIS E SUPORTE", p.ServiceDescription);
        // The proper LC 116 field: cTribNac 010701 → canonical "1.07" (not "01.07"), matching the rule/map keys.
        Assert.Equal("1.07", p.Lc116Subitem);
    }

    [Fact]
    public void Real_nfse_nacional_service_carries_inss_11pct_for_normal_pj()
    {
        var p = new NfseNacionalXmlParser().Parse(XDocument.Parse(RealNfseXml));

        // INSS rules mirror the seed: 11% geral, 3,5% CPRB, Simples Anexo IV 11%, demais Simples isento.
        var rules = new List<WithholdingRule>
        {
            new(WithholdingTaxType.Inss, retains: true, RateSource.Fixed, 0.11m),
            new(WithholdingTaxType.Inss, retains: true, RateSource.Fixed, 0.035m, cprb: true),
            new(WithholdingTaxType.Inss, retains: false, RateSource.Fixed, null, vendorRegime: TaxRegime.SimplesNacional, reason: NonRetentionReason.RegimeExemption),
            new(WithholdingTaxType.Inss, retains: true, RateSource.Fixed, 0.11m, vendorRegime: TaxRegime.SimplesNacional, simplesAnexo: SimplesAnexo.IV),
        };

        // AVATO is opSimpNac=1 (não optante do Simples) → normal PJ (e.g. Lucro Presumido). INSS follows the regime,
        // independent of the LC 116 subitem, over the gross service value (4402,00).
        var ctx = new ResolutionContext(null, TaxRegime.LucroPresumido,
            [new ServiceLine(p.Lc116Subitem, "4304606", null, p.TotalAmount!.Value)]);

        var inss = WithholdingRuleResolver.Resolve(rules, ctx)
            .Where(c => c.TaxType == WithholdingTaxType.Inss)
            .Sum(c => c.Amount);

        output.WriteLine($"INSS={inss}");
        Assert.Equal(484.22m, inss); // 4402,00 × 11%
    }
}
