using System.Text;
using System.Xml.Linq;
using Ulbra.One.Common.Application.Ai;
using Ulbra.One.Common.Domain;
using Ulbra.One.Finance.Application.FiscalDocuments;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Infrastructure.FiscalDocuments;
using Xunit;

namespace Ulbra.One.UnitTests.Finance;

public class FiscalDocumentParserTests
{
    private const string Key44 = "12345678901234567890123456789012345678901234";
    private const string Key50 = "12345678901234567890123456789012345678901234567890";

    private const string NfeXml = $"""
<?xml version="1.0" encoding="UTF-8"?>
<nfeProc xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00">
  <NFe>
    <infNFe Id="NFe{Key44}" versao="4.00">
      <ide>
        <nNF>1234</nNF>
        <serie>1</serie>
        <dhEmi>2026-06-16T14:30:00-03:00</dhEmi>
      </ide>
      <emit>
        <CNPJ>11222333000181</CNPJ>
        <xNome>Codeforge Desenvolvimento de Software LTDA</xNome>
      </emit>
      <dest>
        <CNPJ>11444777000161</CNPJ>
        <xNome>Industria Mecanica Sul-Brasileira LTDA</xNome>
      </dest>
      <total>
        <ICMSTot>
          <vNF>18000.00</vNF>
        </ICMSTot>
      </total>
    </infNFe>
  </NFe>
</nfeProc>
""";

    // NF-e in installments: <cobr> with invoice + 3 duplicatas (dVenc/vDup) and <pag> with one detPag per installment.
    private const string NfeWithCobrXml = $"""
<?xml version="1.0" encoding="UTF-8"?>
<nfeProc xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00">
  <NFe>
    <infNFe Id="NFe{Key44}" versao="4.00">
      <ide><nNF>1234</nNF><serie>1</serie><dhEmi>2026-06-16T14:30:00-03:00</dhEmi></ide>
      <emit><CNPJ>11222333000181</CNPJ><xNome>Codeforge</xNome></emit>
      <dest><CNPJ>11444777000161</CNPJ><xNome>Industria</xNome></dest>
      <total><ICMSTot><vNF>18000.00</vNF></ICMSTot></total>
      <cobr>
        <fat><nFat>001234</nFat><vOrig>18000.00</vOrig><vDesc>0.00</vDesc><vLiq>18000.00</vLiq></fat>
        <dup><nDup>001</nDup><dVenc>2026-07-16</dVenc><vDup>6000.00</vDup></dup>
        <dup><nDup>002</nDup><dVenc>2026-08-16</dVenc><vDup>6000.00</vDup></dup>
        <dup><nDup>003</nDup><dVenc>2026-09-16</dVenc><vDup>6000.00</vDup></dup>
      </cobr>
      <pag>
        <detPag><tPag>15</tPag><vPag>6000.00</vPag></detPag>
        <detPag><tPag>15</tPag><vPag>6000.00</vPag></detPag>
        <detPag><tPag>15</tPag><vPag>6000.00</vPag></detPag>
      </pag>
    </infNFe>
  </NFe>
</nfeProc>
""";

    // NF-e paid upfront: no <cobr>, a single <pag>/detPag summing to vNF.
    private const string NfeAVistaXml = $"""
<?xml version="1.0" encoding="UTF-8"?>
<nfeProc xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00">
  <NFe>
    <infNFe Id="NFe{Key44}" versao="4.00">
      <ide><nNF>1234</nNF><serie>1</serie><dhEmi>2026-06-16T14:30:00-03:00</dhEmi></ide>
      <emit><CNPJ>11222333000181</CNPJ><xNome>Codeforge</xNome></emit>
      <dest><CNPJ>11444777000161</CNPJ><xNome>Industria</xNome></dest>
      <total><ICMSTot><vNF>18000.00</vNF></ICMSTot></total>
      <pag><detPag><tPag>01</tPag><vPag>18000.00</vPag></detPag></pag>
    </infNFe>
  </NFe>
</nfeProc>
""";

    // NF-e with 2 items (det/prod/xProd) → combined description to pre-fill the payable.
    private const string NfeMultiItemXml = $"""
<?xml version="1.0" encoding="UTF-8"?>
<nfeProc xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00">
  <NFe>
    <infNFe Id="NFe{Key44}" versao="4.00">
      <ide><nNF>1234</nNF><serie>1</serie><dhEmi>2026-06-16T14:30:00-03:00</dhEmi></ide>
      <emit><CNPJ>11222333000181</CNPJ><xNome>Codeforge</xNome></emit>
      <dest><CNPJ>11444777000161</CNPJ><xNome>Industria</xNome></dest>
      <det nItem="1"><prod><xProd>Switch 24 portas</xProd><NCM>85176241</NCM><CFOP>5102</CFOP><vProd>900.00</vProd></prod></det>
      <det nItem="2"><prod><xProd>Cabo CAT6 (caixa)</xProd><NCM>85444900</NCM><CFOP>5102</CFOP><vProd>100.00</vProd></prod></det>
      <total><ICMSTot><vNF>1000.00</vNF></ICMSTot></total>
    </infNFe>
  </NFe>
</nfeProc>
""";

    // NF-e with items + fiscal totals (ICMSTot with vICMS/vIPI/vPIS/vCOFINS/vTotTrib) — used by the FiscalSummary test.
    private const string NfeWithFiscalXml = $"""
<?xml version="1.0" encoding="UTF-8"?>
<nfeProc xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00">
  <NFe>
    <infNFe Id="NFe{Key44}" versao="4.00">
      <ide><nNF>1234</nNF><serie>1</serie><dhEmi>2026-06-16T14:30:00-03:00</dhEmi></ide>
      <emit><CNPJ>11222333000181</CNPJ><xNome>Codeforge</xNome></emit>
      <dest><CNPJ>11444777000161</CNPJ><xNome>Industria</xNome></dest>
      <det nItem="1"><prod><xProd>Switch 24 portas</xProd><NCM>85176241</NCM><CFOP>5102</CFOP><vProd>900.00</vProd></prod></det>
      <det nItem="2"><prod><xProd>Cabo CAT6</xProd><NCM>85444900</NCM><CFOP>5102</CFOP><vProd>100.00</vProd></prod></det>
      <total>
        <ICMSTot>
          <vNF>1000.00</vNF>
          <vICMS>90.00</vICMS>
          <vIPI>10.00</vIPI>
          <vPIS>6.50</vPIS>
          <vCOFINS>30.00</vCOFINS>
          <vTotTrib>136.50</vTotTrib>
        </ICMSTot>
      </total>
    </infNFe>
  </NFe>
</nfeProc>
""";

    private const string NfseXml = """
<?xml version="1.0" encoding="UTF-8"?>
<CompNfse xmlns="http://www.abrasf.org.br/nfse.xsd">
  <Nfse versao="2.04">
    <InfNfse Id="NFSE00000001234">
      <Numero>00000001234</Numero>
      <DataEmissao>2026-06-16T14:32:18</DataEmissao>
      <IdentificacaoRps>
        <Numero>1234</Numero>
        <Serie>RPS</Serie>
        <Tipo>1</Tipo>
      </IdentificacaoRps>
      <Servico>
        <Valores>
          <ValorServicos>18000.00</ValorServicos>
        </Valores>
        <ItemListaServico>01.04</ItemListaServico>
        <CodigoCnae>6201501</CodigoCnae>
        <Discriminacao>Desenvolvimento de software sob encomenda — contrato CT-2026-014.</Discriminacao>
      </Servico>
      <PrestadorServico>
        <IdentificacaoPrestador>
          <CpfCnpj><Cnpj>11222333000181</Cnpj></CpfCnpj>
        </IdentificacaoPrestador>
        <RazaoSocial>Codeforge Desenvolvimento de Software LTDA</RazaoSocial>
      </PrestadorServico>
      <TomadorServico>
        <IdentificacaoTomador>
          <CpfCnpj><Cnpj>11444777000161</Cnpj></CpfCnpj>
        </IdentificacaoTomador>
      </TomadorServico>
    </InfNfse>
  </Nfse>
</CompNfse>
""";

    // NFS-e national standard (SEFIN): NFSe → infNFSe (emit) → DPS → infDPS. vLiq (17370.00) differs from
    // vServ (18000.00) on purpose, to prove TotalAmount takes the gross vServ.
    private const string NfseNacionalXml = $"""
<?xml version="1.0" encoding="UTF-8"?>
<NFSe versao="1.01" xmlns="http://www.sped.fazenda.gov.br/nfse">
  <infNFSe Id="NFS{Key50}">
    <nNFSe>60063</nNFSe>
    <dhProc>2026-06-15T18:50:53-03:00</dhProc>
    <emit>
      <CNPJ>11222333000181</CNPJ>
      <xNome>Codeforge Desenvolvimento de Software LTDA</xNome>
      <enderNac><xLgr>Rua Guilherme Kurtz</xLgr><nro>3210</nro><xBairro>Centro</xBairro><cMun>4310538</cMun><UF>RS</UF><CEP>97185000</CEP></enderNac>
      <fone>5133785980</fone>
    </emit>
    <valores><vBC>18000.00</vBC><pAliqAplic>2.00</pAliqAplic><vISSQN>360.00</vISSQN><vLiq>17370.00</vLiq></valores>
    <DPS versao="1.01">
      <infDPS Id="DPS00000000000000000000000000000000000000001">
        <dhEmi>2026-06-15T18:49:34-03:00</dhEmi>
        <serie>001</serie>
        <nDPS>8360128</nDPS>
        <prest><CNPJ>11222333000181</CNPJ></prest>
        <toma><CNPJ>11444777000161</CNPJ><xNome>Industria Mecanica Sul-Brasileira LTDA</xNome></toma>
        <serv><cServ><cTribNac>010701</cTribNac><xDescServ>Servicos digitais e suporte</xDescServ><cNBS>115013000</cNBS></cServ></serv>
        <valores>
          <vServPrest><vServ>18000.00</vServ></vServPrest>
        </valores>
      </infDPS>
    </DPS>
  </infNFSe>
</NFSe>
""";

    // File with only <DPS> (no infNFSe/emit): lenient parse, anchored on infDPS.
    private const string DpsOnlyXml = """
<?xml version="1.0" encoding="UTF-8"?>
<DPS versao="1.01" xmlns="http://www.sped.fazenda.gov.br/nfse">
  <infDPS Id="DPS00000000000000000000000000000000000000003">
    <dhEmi>2026-06-15T18:49:34-03:00</dhEmi>
    <serie>001</serie>
    <nDPS>8360128</nDPS>
    <toma><CNPJ>11444777000161</CNPJ></toma>
    <serv><cServ><xDescServ>Suporte</xDescServ></cServ></serv>
    <valores><vServPrest><vServ>4402.00</vServ></vServPrest></valores>
  </infDPS>
</DPS>
""";

    // NFS-e ABRASF with both ValorServicos and ValorLiquidoNfse (differing) — proves TotalAmount = gross.
    private const string NfseAbrasfGrossXml = """
<?xml version="1.0" encoding="UTF-8"?>
<CompNfse xmlns="http://www.abrasf.org.br/nfse.xsd">
  <Nfse versao="2.04">
    <InfNfse Id="NFSE00000005678">
      <Numero>00000005678</Numero>
      <DataEmissao>2026-06-16T14:32:18</DataEmissao>
      <Servico>
        <Valores>
          <ValorServicos>10000.00</ValorServicos>
          <ValorLiquidoNfse>9000.00</ValorLiquidoNfse>
        </Valores>
        <Discriminacao>Servico.</Discriminacao>
      </Servico>
      <PrestadorServico>
        <IdentificacaoPrestador><CpfCnpj><Cnpj>11222333000181</Cnpj></CpfCnpj></IdentificacaoPrestador>
        <RazaoSocial>Codeforge</RazaoSocial>
      </PrestadorServico>
      <TomadorServico>
        <IdentificacaoTomador><CpfCnpj><Cnpj>11444777000161</Cnpj></CpfCnpj></IdentificacaoTomador>
      </TomadorServico>
    </InfNfse>
  </Nfse>
</CompNfse>
""";

    // NFS-e ABRASF with ISS/municipality fields populated (advisory 17.01, Canoas IBGE 4304606, ISS 5% withheld).
    private const string NfseWithIssXml = """
<?xml version="1.0" encoding="UTF-8"?>
<CompNfse xmlns="http://www.abrasf.org.br/nfse.xsd">
  <Nfse versao="2.04">
    <InfNfse Id="NFSE00000005678">
      <Numero>00000005678</Numero>
      <DataEmissao>2026-06-20T09:15:00</DataEmissao>
      <Servico>
        <Valores>
          <ValorServicos>3000.00</ValorServicos>
          <Aliquota>0.0500</Aliquota>
          <IssRetido>1</IssRetido>
          <ValorIss>150.00</ValorIss>
        </Valores>
        <ItemListaServico>17.01</ItemListaServico>
        <CodigoTributacaoMunicipio>170100</CodigoTributacaoMunicipio>
        <CodigoMunicipio>4304606</CodigoMunicipio>
        <CodigoCnae>6204000</CodigoCnae>
        <Discriminacao>Assessoria e consultoria.</Discriminacao>
      </Servico>
      <PrestadorServico>
        <IdentificacaoPrestador>
          <CpfCnpj><Cnpj>11222333000181</Cnpj></CpfCnpj>
        </IdentificacaoPrestador>
        <RazaoSocial>Consultoria Exemplo LTDA</RazaoSocial>
      </PrestadorServico>
      <TomadorServico>
        <IdentificacaoTomador>
          <CpfCnpj><Cnpj>11444777000161</Cnpj></CpfCnpj>
        </IdentificacaoTomador>
      </TomadorServico>
    </InfNfse>
  </Nfse>
</CompNfse>
""";

    private sealed class DisabledAi : IAiCompletion
    {
        public Task<Result<T>> CompleteAsync<T>(AiPrompt p, AiOptions? o = null, CancellationToken ct = default) where T : class =>
            Task.FromResult(Result.Failure<T>(Error.Failure(AiErrors.Disabled, "off")));
    }

    private static IFiscalDocumentReader NewReader() =>
        new FiscalDocumentReader(
            [new NfeM55XmlParser(), new NfseAbrasfXmlParser(), new NfseNacionalXmlParser()],
            new AiFiscalExtractor(new DisabledAi()));

    private static Stream StreamOf(string xml) => new MemoryStream(Encoding.UTF8.GetBytes(xml));

    [Fact]
    public void Nfe_parser_extracts_product_fields()
    {
        var parser = new NfeM55XmlParser();
        var doc = XDocument.Parse(NfeXml);
        Assert.True(parser.CanParse(doc));

        var p = parser.Parse(doc);
        Assert.Equal(FiscalDocumentFormat.NfeXml, p.Format);
        Assert.Equal(Key44, p.AccessKey);
        Assert.Equal("1234", p.DocumentNumber);
        Assert.Equal(new DateOnly(2026, 6, 16), p.IssueDate);
        Assert.Equal(18000.00m, p.TotalAmount);
        Assert.Equal("11222333000181", p.IssuerDocument);
        Assert.Equal("Codeforge Desenvolvimento de Software LTDA", p.IssuerName);
        Assert.Equal("11444777000161", p.RecipientDocument);
        Assert.Empty(p.Installments); // no <cobr> → paid upfront
    }

    [Fact]
    public void Nfe_parser_extracts_installments_from_cobr()
    {
        var p = new NfeM55XmlParser().Parse(XDocument.Parse(NfeWithCobrXml));
        Assert.Equal(3, p.Installments.Count);
        Assert.Equal(new DateOnly(2026, 7, 16), p.Installments[0].DueDate);
        Assert.Equal(6000.00m, p.Installments[0].Amount);
        Assert.Equal(new DateOnly(2026, 8, 16), p.Installments[1].DueDate);
        Assert.Equal(new DateOnly(2026, 9, 16), p.Installments[2].DueDate);
        Assert.Equal(18000.00m, p.Installments.Sum(i => i.Amount));
    }

    [Fact]
    public void Nfe_joins_item_descriptions_into_service_description()
    {
        var p = new NfeM55XmlParser().Parse(XDocument.Parse(NfeMultiItemXml));
        Assert.Equal("Switch 24 portas; Cabo CAT6 (caixa)", p.ServiceDescription);
    }

    [Fact]
    public void Nfe_avista_has_no_installments()
    {
        var p = new NfeM55XmlParser().Parse(XDocument.Parse(NfeAVistaXml));
        Assert.Empty(p.Installments);
        Assert.Equal(18000.00m, p.TotalAmount);
    }

    [Fact]
    public void Nfse_parser_extracts_service_fields()
    {
        var parser = new NfseAbrasfXmlParser();
        var doc = XDocument.Parse(NfseXml);
        Assert.True(parser.CanParse(doc));

        var p = parser.Parse(doc);
        Assert.Equal(FiscalDocumentFormat.NfseXml, p.Format);
        Assert.Null(p.AccessKey);
        Assert.Equal("00000001234", p.DocumentNumber);
        Assert.Equal(new DateOnly(2026, 6, 16), p.IssueDate);
        Assert.Equal(18000.00m, p.TotalAmount);
        Assert.Equal("11222333000181", p.IssuerDocument);
        Assert.Equal("Codeforge Desenvolvimento de Software LTDA", p.IssuerName);
        Assert.Equal("11444777000161", p.RecipientDocument);
        Assert.Empty(p.Installments); // ABRASF NFS-e carries no installments
        Assert.Equal("1234", p.RpsNumber);
        Assert.Equal("RPS", p.RpsSeries);
        Assert.Equal("6201501", p.CnaeCode);
        Assert.Contains("CT-2026-014", p.ServiceDescription);
        // LC 116 subitem is reliable (comes in the invoice); the other ISS fields are optional and absent in this sample.
        Assert.Equal("01.04", p.Lc116Subitem);
        Assert.Null(p.MunicipalServiceCode);
        Assert.Null(p.ServiceMunicipalityIbge);
        Assert.Null(p.NotaIssRate);
        Assert.Null(p.IssRetidoNaNota);
    }

    [Fact]
    public void Nfse_parser_extracts_iss_and_municipal_fields()
    {
        var p = new NfseAbrasfXmlParser().Parse(XDocument.Parse(NfseWithIssXml));
        Assert.Equal("17.01", p.Lc116Subitem);
        Assert.Equal("170100", p.MunicipalServiceCode);
        Assert.Equal("4304606", p.ServiceMunicipalityIbge);
        Assert.Equal(0.05m, p.NotaIssRate);
        Assert.True(p.IssRetidoNaNota);
    }

    [Fact]
    public void Nfse_abrasf_uses_gross_total()
    {
        var p = new NfseAbrasfXmlParser().Parse(XDocument.Parse(NfseAbrasfGrossXml));
        Assert.Equal(10000.00m, p.TotalAmount); // ValorServicos (gross), not ValorLiquidoNfse (9000.00)
    }

    [Fact]
    public void Nfse_abrasf_trims_item_lista_servico()
    {
        // Stray whitespace around ItemListaServico must not break the exact-match against withholding rules.
        var xml = """
<?xml version="1.0" encoding="UTF-8"?>
<CompNfse xmlns="http://www.abrasf.org.br/nfse.xsd">
  <Nfse versao="2.04">
    <InfNfse Id="NFSE00000005679">
      <Numero>00000005679</Numero>
      <DataEmissao>2026-06-20T09:15:00</DataEmissao>
      <Servico>
        <Valores><ValorServicos>3000.00</ValorServicos></Valores>
        <ItemListaServico>  17.01  </ItemListaServico>
        <Discriminacao>Servico.</Discriminacao>
      </Servico>
      <PrestadorServico>
        <IdentificacaoPrestador><CpfCnpj><Cnpj>11222333000181</Cnpj></CpfCnpj></IdentificacaoPrestador>
      </PrestadorServico>
    </InfNfse>
  </Nfse>
</CompNfse>
""";
        var p = new NfseAbrasfXmlParser().Parse(XDocument.Parse(xml));
        Assert.Equal("17.01", p.Lc116Subitem);
    }

    [Fact]
    public void Parsers_reject_the_other_standard()
    {
        // NF-e parser rejects both NFS-e standards.
        Assert.False(new NfeM55XmlParser().CanParse(XDocument.Parse(NfseXml)));
        Assert.False(new NfeM55XmlParser().CanParse(XDocument.Parse(NfseNacionalXml)));
        // ABRASF parser rejects NF-e and the national NFS-e.
        Assert.False(new NfseAbrasfXmlParser().CanParse(XDocument.Parse(NfeXml)));
        Assert.False(new NfseAbrasfXmlParser().CanParse(XDocument.Parse(NfseNacionalXml)));
        // National parser rejects NF-e and ABRASF NFS-e.
        Assert.False(new NfseNacionalXmlParser().CanParse(XDocument.Parse(NfeXml)));
        Assert.False(new NfseNacionalXmlParser().CanParse(XDocument.Parse(NfseXml)));
    }

    [Fact]
    public void Nfse_nacional_parser_extracts_service_fields()
    {
        var parser = new NfseNacionalXmlParser();
        var doc = XDocument.Parse(NfseNacionalXml);
        Assert.True(parser.CanParse(doc));

        var p = parser.Parse(doc);
        Assert.Equal(FiscalDocumentFormat.NfseXml, p.Format);
        Assert.Equal(Key50, p.AccessKey);          // 50-digit national key, "NFS" prefix stripped
        Assert.Equal(50, p.AccessKey!.Length);
        Assert.Equal("60063", p.DocumentNumber);
        Assert.Equal(new DateOnly(2026, 6, 15), p.IssueDate);
        Assert.Equal(18000.00m, p.TotalAmount);    // gross (vServ), not vLiq (17370.00)
        Assert.Equal("11222333000181", p.IssuerDocument);
        Assert.Equal("Codeforge Desenvolvimento de Software LTDA", p.IssuerName);
        Assert.Equal("11444777000161", p.RecipientDocument);
        Assert.Equal("8360128", p.RpsNumber);
        Assert.Equal("001", p.RpsSeries);
        Assert.Null(p.CnaeCode);                   // national has no CNAE
        Assert.Equal("01.07", p.Lc116Subitem);     // derived from cTribNac 010701 (item 01, subitem 07)
        Assert.Equal("5133785980", p.IssuerPhone);
        Assert.NotNull(p.IssuerAddress);
        Assert.Equal("RS", p.IssuerAddress!.State);
        Assert.Null(p.IssuerAddress.City);         // only the IBGE code is in the XML, no city name
    }

    [Fact]
    public void Nfse_nacional_parses_bare_dps_without_throwing()
    {
        var p = new NfseNacionalXmlParser().Parse(XDocument.Parse(DpsOnlyXml));
        Assert.Equal(FiscalDocumentFormat.NfseXml, p.Format);
        Assert.Null(p.DocumentNumber);             // no infNFSe/nNFSe
        Assert.Equal(4402.00m, p.TotalAmount);     // vServ from infDPS
        Assert.Equal("11444777000161", p.RecipientDocument);
        Assert.Null(p.Lc116Subitem);               // no cTribNac in this bare DPS → no subitem
    }

    [Fact]
    public void Nfse_nacional_leaves_lc116_null_when_ctribnac_malformed()
    {
        // cTribNac must be exactly 6 numeric digits (item + subitem + national breakdown); a malformed value
        // yields no subitem, so no withholding is fabricated (best-effort — never guess a subitem).
        var malformed = """
<?xml version="1.0" encoding="UTF-8"?>
<DPS versao="1.01" xmlns="http://www.sped.fazenda.gov.br/nfse">
  <infDPS Id="DPS00000000000000000000000000000000000000009">
    <toma><CNPJ>11444777000161</CNPJ></toma>
    <serv><cServ><cTribNac>0107</cTribNac><xDescServ>Suporte</xDescServ></cServ></serv>
    <valores><vServPrest><vServ>1000.00</vServ></vServPrest></valores>
  </infDPS>
</DPS>
""";
        var p = new NfseNacionalXmlParser().Parse(XDocument.Parse(malformed));
        Assert.Null(p.Lc116Subitem);
    }

    [Fact]
    public async Task Reader_detects_nfse_nacional_by_namespace()
    {
        var parsed = await NewReader().ReadAsync("nota.xml", StreamOf(NfseNacionalXml), default);
        Assert.NotNull(parsed);
        Assert.Equal(FiscalDocumentFormat.NfseXml, parsed!.Format);
        Assert.Equal("60063", parsed.DocumentNumber);
    }

    [Fact]
    public async Task Reader_detects_nfe_by_namespace()
    {
        var parsed = await NewReader().ReadAsync("nota.xml", StreamOf(NfeXml), default);
        Assert.NotNull(parsed);
        Assert.Equal(FiscalDocumentFormat.NfeXml, parsed!.Format);
    }

    [Fact]
    public async Task Reader_detects_nfse_by_namespace()
    {
        var parsed = await NewReader().ReadAsync("nota.xml", StreamOf(NfseXml), default);
        Assert.NotNull(parsed);
        Assert.Equal(FiscalDocumentFormat.NfseXml, parsed!.Format);
    }

    [Fact]
    public async Task Reader_returns_pdf_format_for_pdf()
    {
        var parsed = await NewReader().ReadAsync("nota.pdf", new MemoryStream([0x25, 0x50, 0x44, 0x46]), default);
        Assert.NotNull(parsed);
        Assert.Equal(FiscalDocumentFormat.Pdf, parsed!.Format);
    }

    [Fact]
    public async Task Reader_returns_null_for_unknown_namespace()
    {
        const string unknown = """<?xml version="1.0"?><Foo xmlns="urn:unknown:schema"><Bar/></Foo>""";
        var parsed = await NewReader().ReadAsync("nota.xml", StreamOf(unknown), default);
        Assert.Null(parsed);
    }

    [Fact]
    public async Task Reader_returns_null_for_unknown_extension()
    {
        var parsed = await NewReader().ReadAsync("nota.txt", StreamOf(NfeXml), default);
        Assert.Null(parsed);
    }

    [Fact]
    public void NfeParser_extracts_fiscal_summary()
    {
        var parsed = new NfeM55XmlParser().Parse(XDocument.Parse(NfeWithFiscalXml));
        Assert.NotNull(parsed.Fiscal);
        Assert.NotEmpty(parsed.Fiscal!.Cfops);   // sample has at least one item with a CFOP
        Assert.NotNull(parsed.Fiscal.TotalTaxes); // vTotTrib present in ICMSTot
    }
}
