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

    // NF-e parcelada: <cobr> com fatura + 3 duplicatas (dVenc/vDup) e <pag> com um detPag por parcela.
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

    // NF-e à vista: sem <cobr>, <pag> com um único detPag somando vNF.
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

    // NF-e com 2 itens (det/prod/xProd) → descrição combinada para preencher a conta a pagar.
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

    // NF-e com itens + totais fiscais (ICMSTot com vICMS/vIPI/vPIS/vCOFINS/vTotTrib) — usado no teste de FiscalSummary.
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
        new FiscalDocumentReader([new NfeM55XmlParser(), new NfseAbrasfXmlParser()], new AiFiscalExtractor(new DisabledAi()));

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
        Assert.Empty(p.Installments); // sem <cobr> → à vista
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
        Assert.Empty(p.Installments); // NFS-e ABRASF não traz duplicatas
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
    public void Parsers_reject_the_other_standard()
    {
        Assert.False(new NfeM55XmlParser().CanParse(XDocument.Parse(NfseXml)));
        Assert.False(new NfseAbrasfXmlParser().CanParse(XDocument.Parse(NfeXml)));
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
