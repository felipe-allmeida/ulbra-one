using System.Xml.Linq;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Infrastructure.FiscalDocuments;
using Xunit;
using Xunit.Abstractions;

namespace Ulbra.One.UnitTests.Finance;

/// <summary>
/// Reproduction test against a real-world NFA-e (Nota Fiscal Avulsa eletrônica, mod 55) emitted by SEFAZ-RS.
/// The infNFe block is verbatim from the sample the user reported as "not parsed"; Signature/protNFe siblings
/// are dropped because the parser only reads inside infNFe.
/// </summary>
public class FiscalDocumentRealSampleTests(ITestOutputHelper output)
{
    private const string RealNfaeXml = """
<nfeProc versao="4.00" xmlns="http://www.portalfiscal.inf.br/nfe"><NFe xmlns="http://www.portalfiscal.inf.br/nfe"><infNFe versao="4.00" Id="NFe43260687958674000181558900673725291034404737"><ide><cUF>43</cUF><cNF>03440473</cNF><natOp>Venda de Mercadoria</natOp><mod>55</mod><serie>890</serie><nNF>67372529</nNF><dhEmi>2026-06-08T17:31:00-03:00</dhEmi><tpNF>1</tpNF><idDest>1</idDest><cMunFG>4314902</cMunFG><tpImp>1</tpImp><tpEmis>1</tpEmis><cDV>7</cDV><tpAmb>1</tpAmb><finNFe>1</finNFe><indFinal>1</indFinal><indPres>1</indPres><indIntermed>0</indIntermed><procEmi>1</procEmi><verProc>SVRS20260408200851</verProc></ide><emit><CNPJ>11222333000181</CNPJ><xNome>11.222.333 EMITENTE REDACTED</xNome><enderEmit><xLgr>AVENIDA REDACTED</xLgr><nro>704</nro><xBairro>IPANEMA</xBairro><cMun>4314902</cMun><xMun>PORTO ALEGRE</xMun><UF>RS</UF><CEP>90000000</CEP><cPais>1058</cPais><fone>51900000002</fone></enderEmit><IE>8000000000</IE><CRT>4</CRT></emit><avulsa><CNPJ>87958674000181</CNPJ><xOrgao>SEFAZ-RS</xOrgao><matr>99999999</matr><xAgente>NFA-e WEB</xAgente><UF>RS</UF><repEmi>SEFAZ-RS</repEmi></avulsa><dest><CNPJ>88332580000670</CNPJ><xNome>ULBRA S.A.</xNome><enderDest><xLgr>AV FARROUPILHA</xLgr><nro>8001</nro><xBairro>São José</xBairro><cMun>4304606</cMun><xMun>CANOAS</xMun><UF>RS</UF><CEP>92425900</CEP><cPais>1058</cPais><xPais>BRASIL</xPais></enderDest><indIEDest>9</indIEDest></dest><det nItem="1"><prod><cProd>001</cProd><cEAN>SEM GTIN</cEAN><xProd>ACCESS POINT UBIQUITI UNIFI U7 PRO</xProd><NCM>85176241</NCM><CFOP>5102</CFOP><uCom>UN</uCom><qCom>10.0000</qCom><vUnCom>1550.0000</vUnCom><vProd>15500.00</vProd><cEANTrib>SEM GTIN</cEANTrib><uTrib>UN</uTrib><qTrib>10.0000</qTrib><vUnTrib>1550.0000</vUnTrib><indTot>1</indTot></prod><imposto><ICMS><ICMSSN102><orig>2</orig><CSOSN>102</CSOSN></ICMSSN102></ICMS><PIS><PISNT><CST>07</CST></PISNT></PIS><COFINS><COFINSNT><CST>07</CST></COFINSNT></COFINS></imposto></det><total><ICMSTot><vBC>0.00</vBC><vICMS>0.00</vICMS><vICMSDeson>0.00</vICMSDeson><vFCP>0.00</vFCP><vBCST>0.00</vBCST><vST>0.00</vST><vFCPST>0.00</vFCPST><vFCPSTRet>0.00</vFCPSTRet><vProd>15500.00</vProd><vFrete>0.00</vFrete><vSeg>0.00</vSeg><vDesc>0.00</vDesc><vII>0.00</vII><vIPI>0.00</vIPI><vIPIDevol>0.00</vIPIDevol><vPIS>0.00</vPIS><vCOFINS>0.00</vCOFINS><vOutro>0.00</vOutro><vNF>15500.00</vNF><vTotTrib>0.00</vTotTrib></ICMSTot></total><transp><modFrete>9</modFrete></transp><pag><detPag><indPag>0</indPag><tPag>99</tPag><xPag>Pix - Chave 51 900000002</xPag><vPag>15500.00</vPag></detPag></pag><infAdic><infAdFisco>[ *** Login Operador  e-CNPJ: 11.222.333/0001-81 - 11.222.333 EMITENTE REDACTED; ]</infAdFisco></infAdic></infNFe></NFe></nfeProc>
""";

    [Fact]
    public void Real_nfae_extracts_all_fields()
    {
        var parser = new NfeM55XmlParser();
        var doc = XDocument.Parse(RealNfaeXml);

        Assert.True(parser.CanParse(doc));
        var p = parser.Parse(doc);

        output.WriteLine($"Format={p.Format}");
        output.WriteLine($"AccessKey={p.AccessKey ?? "<null>"}");
        output.WriteLine($"DocumentNumber={p.DocumentNumber ?? "<null>"}");
        output.WriteLine($"IssueDate={p.IssueDate?.ToString() ?? "<null>"}");
        output.WriteLine($"TotalAmount={p.TotalAmount?.ToString() ?? "<null>"}");
        output.WriteLine($"IssuerDocument={p.IssuerDocument ?? "<null>"}");
        output.WriteLine($"IssuerName={p.IssuerName ?? "<null>"}");
        output.WriteLine($"RecipientDocument={p.RecipientDocument ?? "<null>"}");
        output.WriteLine($"Installments={p.Installments.Count}");

        Assert.Equal(FiscalDocumentFormat.NfeXml, p.Format);
        Assert.Equal("43260687958674000181558900673725291034404737", p.AccessKey);
        Assert.Equal("67372529", p.DocumentNumber);
        Assert.Equal(new DateOnly(2026, 6, 8), p.IssueDate);
        Assert.Equal(15500.00m, p.TotalAmount);
        Assert.Equal("11222333000181", p.IssuerDocument);
        Assert.Equal("11.222.333 EMITENTE REDACTED", p.IssuerName);
        Assert.Equal("88332580000670", p.RecipientDocument);
        Assert.Empty(p.Installments);
        // NF-e (produto): the item description (det/prod/xProd) is surfaced as the document description.
        Assert.Equal("ACCESS POINT UBIQUITI UNIFI U7 PRO", p.ServiceDescription);

        // Issuer contact data (used to pre-fill a vendor registration when there's no match).
        Assert.Equal("51900000002", p.IssuerPhone);
        Assert.NotNull(p.IssuerAddress);
        Assert.Equal("AVENIDA REDACTED", p.IssuerAddress!.Street);
        Assert.Equal("704", p.IssuerAddress.Number);
        Assert.Equal("IPANEMA", p.IssuerAddress.Neighborhood);
        Assert.Equal("PORTO ALEGRE", p.IssuerAddress.City);
        Assert.Equal("RS", p.IssuerAddress.State);
        Assert.Equal("90000000", p.IssuerAddress.Cep);
    }
}
