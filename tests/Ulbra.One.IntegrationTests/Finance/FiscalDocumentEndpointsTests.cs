using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Ulbra.One.IntegrationTests.Finance;

[Collection("Postgres")]
public class FiscalDocumentEndpointsTests(PostgresFixture fixture)
{
    private const string IssuerCnpjDigits = "11222333000181";

    private const string NfeXml = """
<?xml version="1.0" encoding="UTF-8"?>
<nfeProc xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00">
  <NFe>
    <infNFe Id="NFe12345678901234567890123456789012345678901234" versao="4.00">
      <ide><nNF>1234</nNF><serie>1</serie><dhEmi>2026-06-16T14:30:00-03:00</dhEmi></ide>
      <emit><CNPJ>11222333000181</CNPJ><xNome>Codeforge Desenvolvimento de Software LTDA</xNome></emit>
      <dest><CNPJ>11444777000161</CNPJ><xNome>Industria Mecanica Sul LTDA</xNome></dest>
      <total><ICMSTot><vNF>18000.00</vNF></ICMSTot></total>
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
      <Servico><Valores><ValorServicos>18000.00</ValorServicos></Valores></Servico>
      <PrestadorServico>
        <IdentificacaoPrestador><CpfCnpj><Cnpj>11222333000181</Cnpj></CpfCnpj></IdentificacaoPrestador>
        <RazaoSocial>Codeforge Desenvolvimento de Software LTDA</RazaoSocial>
      </PrestadorServico>
      <TomadorServico>
        <IdentificacaoTomador><CpfCnpj><Cnpj>11444777000161</Cnpj></CpfCnpj></IdentificacaoTomador>
      </TomadorServico>
    </InfNfse>
  </Nfse>
</CompNfse>
""";

    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private sealed record Seed(string CompanyId, string CostCenterId, string VendorId);

    private static async Task<Seed> SeedAsync(HttpClient client)
    {
        var cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        var companyRes = await client.PostAsJsonAsync("/api/companies", new { legalName = "ULBRA Teste", cnpj });
        companyRes.EnsureSuccessStatusCode();
        var companyId = (await ReadJson(companyRes)).GetProperty("id").GetString()!;

        var ccRes = await client.PostAsJsonAsync("/api/cost-centers",
            new { companyId, code = "CC1", name = "TI", annualBudget = 1_000_000m, autoApprovalLimit = 0m, unitIds = Array.Empty<string>() });
        ccRes.EnsureSuccessStatusCode();
        var costCenterId = (await ReadJson(ccRes)).GetProperty("id").GetString()!;

        // Vendor whose normalized document equals the issuer CNPJ in the sample XMLs.
        var vendorRes = await client.PostAsJsonAsync("/api/vendors", new
        {
            companyId,
            personType = 1,
            name = "Codeforge Desenvolvimento de Software LTDA",
            tradeName = "Codeforge",
            document = "11.222.333/0001-81",
            email = "fiscal@codeforge.com.br",
            phone = "5133451200",
            address = (object?)null,
            paymentTerms = "30 dias",
            bankInfo = (string?)null,
        });
        vendorRes.EnsureSuccessStatusCode();
        var vendorId = (await ReadJson(vendorRes)).GetProperty("id").GetString()!;

        return new Seed(companyId, costCenterId, vendorId);
    }

    private static async Task<string> CreatePayableAsync(HttpClient client, Seed seed)
    {
        var res = await client.PostAsJsonAsync("/api/payables", new
        {
            companyId = seed.CompanyId,
            vendorId = seed.VendorId,
            costCenterId = seed.CostCenterId,
            description = "Conta com NF",
            installments = new[] { new { dueDate = "2026-07-10", amount = 18000m } },
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await ReadJson(res)).GetProperty("id").GetString()!;
    }

    private static MultipartFormDataContent FileContent(string text, string fileName, string contentType)
    {
        var content = new MultipartFormDataContent();
        var bytes = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        bytes.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(bytes, "file", fileName);
        return content;
    }

    [Fact]
    public async Task Parse_Nfse_Returns_Service_Fields_And_Matches_Vendor()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);

        using var content = FileContent(NfseXml, "nfse.xml", "application/xml");
        var res = await client.PostAsync($"/api/payables/fiscal-document/parse?companyId={seed.CompanyId}", content);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await ReadJson(res);
        Assert.Equal("NfseXml", body.GetProperty("format").GetString());
        Assert.Equal("00000001234", body.GetProperty("documentNumber").GetString());
        Assert.Equal(18000.00m, body.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(IssuerCnpjDigits, body.GetProperty("issuerDocument").GetString());
        Assert.Equal(seed.VendorId, body.GetProperty("matchedVendorId").GetString());
    }

    [Fact]
    public async Task Parse_Nfe_Returns_Product_Fields()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);

        using var content = FileContent(NfeXml, "nfe.xml", "application/xml");
        var res = await client.PostAsync($"/api/payables/fiscal-document/parse?companyId={seed.CompanyId}", content);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await ReadJson(res);
        Assert.Equal("NfeXml", body.GetProperty("format").GetString());
        Assert.Equal("1234", body.GetProperty("documentNumber").GetString());
        Assert.Equal(seed.VendorId, body.GetProperty("matchedVendorId").GetString());
    }

    [Fact]
    public async Task Parse_UnknownNamespace_Returns_400_UnsupportedFormat()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);

        using var content = FileContent("""<?xml version="1.0"?><Foo xmlns="urn:unknown"><Bar/></Foo>""", "x.xml", "application/xml");
        var res = await client.PostAsync($"/api/payables/fiscal-document/parse?companyId={seed.CompanyId}", content);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("FiscalDocument.UnsupportedFormat", (await ReadJson(res)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Parse_Pdf_Returns_Service_Mock()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);

        using var content = FileContent("%PDF-1.4\n%%EOF\n", "nota.pdf", "application/pdf");
        var res = await client.PostAsync($"/api/payables/fiscal-document/parse?companyId={seed.CompanyId}", content);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await ReadJson(res);
        Assert.Equal("PdfMock", body.GetProperty("format").GetString());
    }

    [Fact]
    public async Task Upload_Persists_Document_And_Replaces_On_Reupload()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var payableId = await CreatePayableAsync(client, seed);

        using var first = FileContent(NfseXml, "nfse.xml", "application/xml");
        var res = await client.PostAsync($"/api/payables/{payableId}/fiscal-document", first);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var body = await ReadJson(res);
        Assert.Equal("NfseXml", body.GetProperty("format").GetString());
        Assert.Equal("00000001234", body.GetProperty("documentNumber").GetString());

        // Re-upload with a NF-e replaces the existing one (unique per payable) — no 409.
        using var second = FileContent(NfeXml, "nfe.xml", "application/xml");
        var res2 = await client.PostAsync($"/api/payables/{payableId}/fiscal-document", second);
        Assert.Equal(HttpStatusCode.Created, res2.StatusCode);
        Assert.Equal("NfeXml", (await ReadJson(res2)).GetProperty("format").GetString());
    }

    [Fact]
    public async Task Upload_To_NonExistent_Payable_Returns_404()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        using var content = FileContent(NfseXml, "nfse.xml", "application/xml");
        var res = await client.PostAsync($"/api/payables/payable_{Guid.NewGuid():N}/fiscal-document", content);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("Payable.NotFound", (await ReadJson(res)).GetProperty("title").GetString());
    }
}
