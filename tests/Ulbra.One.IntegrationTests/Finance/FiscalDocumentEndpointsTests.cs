using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;

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

    // NFS-e Padrão Nacional (SEFIN). Chave de 50 dígitos no infNFSe/@Id (prefixo NFS); emit = prestador
    // com o mesmo CNPJ do vendor semeado; sem retenção (tpRetISSQN=1) → total bruto == vServ.
    private const string NfseNacionalAccessKey = "12345678901234567890123456789012345678901234567890";
    private const string NfseNacionalXml = """
<?xml version="1.0" encoding="UTF-8"?>
<NFSe versao="1.01" xmlns="http://www.sped.fazenda.gov.br/nfse">
  <infNFSe Id="NFS12345678901234567890123456789012345678901234567890">
    <nNFSe>60063</nNFSe>
    <dhProc>2026-06-15T18:50:53-03:00</dhProc>
    <emit><CNPJ>11222333000181</CNPJ><xNome>Codeforge Desenvolvimento de Software LTDA</xNome></emit>
    <valores><vBC>18000.00</vBC><pAliqAplic>2.00</pAliqAplic><vISSQN>360.00</vISSQN><vLiq>18000.00</vLiq></valores>
    <DPS versao="1.01">
      <infDPS Id="DPS00000000000000000000000000000000000000001">
        <dhEmi>2026-06-15T18:49:34-03:00</dhEmi>
        <serie>001</serie>
        <nDPS>8360128</nDPS>
        <toma><CNPJ>11444777000161</CNPJ></toma>
        <serv><cServ><xDescServ>Servicos digitais e suporte</xDescServ></cServ></serv>
        <valores>
          <vServPrest><vServ>18000.00</vServ></vServPrest>
          <trib><tribMun><tribISSQN>1</tribISSQN><tpRetISSQN>1</tpRetISSQN></tribMun></trib>
        </valores>
      </infDPS>
    </DPS>
  </infNFSe>
</NFSe>
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

    /// <summary>
    /// Seeds a blob directly using <see cref="IFileStorageService.SaveAsync"/> (bypasses the presigned URL path
    /// which is not available with the <c>LocalFileStorageService</c> used in the test environment).
    /// Returns the storage key.
    /// </summary>
    private static async Task<string> SeedBlobAsync(ApiFactory factory, string xml, string fileName)
    {
        using var scope = factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
        var bytes = Encoding.UTF8.GetBytes(xml);
        using var stream = new MemoryStream(bytes);
        return await storage.SaveAsync(fileName, stream);
    }

    [Fact]
    public async Task Parse_Nfse_Returns_Service_Fields_And_Matches_Vendor()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var key = await SeedBlobAsync(factory, NfseXml, "nfse.xml");

        var res = await client.PostAsJsonAsync(
            $"/api/payables/fiscal-document/parse?companyId={seed.CompanyId}",
            new { key, fileName = "nfse.xml" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await ReadJson(res);
        Assert.Equal("NfseXml", body.GetProperty("format").GetString());
        Assert.Equal("00000001234", body.GetProperty("documentNumber").GetString());
        Assert.Equal(18000.00m, body.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(IssuerCnpjDigits, body.GetProperty("issuerDocument").GetString());
        Assert.Equal(seed.VendorId, body.GetProperty("matchedVendorId").GetString());
    }

    [Fact]
    public async Task Parse_Nfe_Returns_Product_Fields_With_FiscalSummary()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var key = await SeedBlobAsync(factory, NfeXml, "nfe.xml");

        var res = await client.PostAsJsonAsync(
            $"/api/payables/fiscal-document/parse?companyId={seed.CompanyId}",
            new { key, fileName = "nfe.xml" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await ReadJson(res);
        Assert.Equal("NfeXml", body.GetProperty("format").GetString());
        Assert.Equal("1234", body.GetProperty("documentNumber").GetString());
        Assert.Equal(18000.00m, body.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(seed.VendorId, body.GetProperty("matchedVendorId").GetString());
        // NF-e should have fiscal summary (even if empty lists for this minimal XML)
        Assert.True(body.TryGetProperty("fiscal", out _), "NF-e should include fiscal summary");
    }

    [Fact]
    public async Task Parse_NfseNacional_Returns_Service_Fields_And_Matches_Vendor()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var key = await SeedBlobAsync(factory, NfseNacionalXml, "nfse-nacional.xml");

        var res = await client.PostAsJsonAsync(
            $"/api/payables/fiscal-document/parse?companyId={seed.CompanyId}",
            new { key, fileName = "nfse-nacional.xml" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await ReadJson(res);
        Assert.Equal("NfseXml", body.GetProperty("format").GetString());
        Assert.Equal("60063", body.GetProperty("documentNumber").GetString());
        Assert.Equal(18000.00m, body.GetProperty("totalAmount").GetDecimal());   // bruto (vServ)
        Assert.Equal(IssuerCnpjDigits, body.GetProperty("issuerDocument").GetString());
        Assert.Equal(seed.VendorId, body.GetProperty("matchedVendorId").GetString());
    }

    [Fact]
    public async Task Upload_NfseNacional_Persists_50Digit_AccessKey()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var payableId = await CreatePayableAsync(client, seed);

        var bytes = Encoding.UTF8.GetBytes(NfseNacionalXml);
        var key = await SeedBlobAsync(factory, NfseNacionalXml, "nfse-nacional.xml");
        var res = await client.PostAsJsonAsync(
            $"/api/payables/{payableId}/fiscal-document",
            new { key, fileName = "nfse-nacional.xml", contentType = "application/xml", sizeBytes = bytes.Length });

        // 50-digit national access key persists without truncation (access_key widened 44->50, no 22001).
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var body = await ReadJson(res);
        Assert.Equal("NfseXml", body.GetProperty("format").GetString());
        Assert.Equal(NfseNacionalAccessKey, body.GetProperty("accessKey").GetString());
    }

    [Fact]
    public async Task Parse_UnknownKey_Returns_400_NotFound()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);

        var res = await client.PostAsJsonAsync(
            $"/api/payables/fiscal-document/parse?companyId={seed.CompanyId}",
            new { key = "nonexistent-key.xml", fileName = "x.xml" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("FiscalDocument.NotFound", (await ReadJson(res)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Parse_UnknownXmlNamespace_Returns_400_UnsupportedFormat()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var key = await SeedBlobAsync(factory, """<?xml version="1.0"?><Foo xmlns="urn:unknown"><Bar/></Foo>""", "x.xml");

        var res = await client.PostAsJsonAsync(
            $"/api/payables/fiscal-document/parse?companyId={seed.CompanyId}",
            new { key, fileName = "x.xml" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("FiscalDocument.UnsupportedFormat", (await ReadJson(res)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Upload_Persists_Document_And_Replaces_On_Reupload()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var payableId = await CreatePayableAsync(client, seed);

        var nfseBytes = Encoding.UTF8.GetBytes(NfseXml);
        var key1 = await SeedBlobAsync(factory, NfseXml, "nfse.xml");
        var res = await client.PostAsJsonAsync(
            $"/api/payables/{payableId}/fiscal-document",
            new { key = key1, fileName = "nfse.xml", contentType = "application/xml", sizeBytes = nfseBytes.Length });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var body = await ReadJson(res);
        Assert.Equal("NfseXml", body.GetProperty("format").GetString());
        Assert.Equal("00000001234", body.GetProperty("documentNumber").GetString());

        // Re-upload with a NF-e replaces the existing one (unique per payable) — no 409.
        var nfeBytes = Encoding.UTF8.GetBytes(NfeXml);
        var key2 = await SeedBlobAsync(factory, NfeXml, "nfe.xml");
        var res2 = await client.PostAsJsonAsync(
            $"/api/payables/{payableId}/fiscal-document",
            new { key = key2, fileName = "nfe.xml", contentType = "application/xml", sizeBytes = nfeBytes.Length });
        Assert.Equal(HttpStatusCode.Created, res2.StatusCode);
        Assert.Equal("NfeXml", (await ReadJson(res2)).GetProperty("format").GetString());
    }

    [Fact]
    public async Task Upload_To_NonExistent_Payable_Returns_404()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var key = await SeedBlobAsync(factory, NfseXml, "nfse.xml");

        var res = await client.PostAsJsonAsync(
            $"/api/payables/payable_{Guid.NewGuid():N}/fiscal-document",
            new { key, fileName = "nfse.xml", contentType = "application/xml", sizeBytes = 1L });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("Payable.NotFound", (await ReadJson(res)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Upload_With_Missing_Key_Returns_400_NotFound()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var payableId = await CreatePayableAsync(client, seed);

        var res = await client.PostAsJsonAsync(
            $"/api/payables/{payableId}/fiscal-document",
            new { key = "ghost-key.xml", fileName = "nfse.xml", contentType = "application/xml", sizeBytes = 1L });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("FiscalDocument.NotFound", (await ReadJson(res)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Parse_BlobOverLimit_Returns_400_TooLarge()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);

        // Seed a blob just over 25 MB using IFileStorageService directly.
        using var scope = factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
        using var ms = new MemoryStream(new byte[26 * 1024 * 1024]);
        var key = await storage.SaveAsync("big.xml", ms);

        var res = await client.PostAsJsonAsync(
            $"/api/payables/fiscal-document/parse?companyId={seed.CompanyId}",
            new { key, fileName = "big.xml" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("FiscalDocument.TooLarge", (await ReadJson(res)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task UploadUrl_Returns_400_PresignNotSupported_On_Local_Storage()
    {
        // In the test environment, LocalFileStorageService is used (no AzureBlob provider configured),
        // which throws NotSupportedException. The endpoint catches this and returns a 400 with
        // Storage.PresignNotSupported so the frontend can fall back to direct multipart upload.
        await using var factory = new ApiFactory(fixture.ConnectionString);
        var client = await factory.CreateAdminClientAsync();

        var res = await client.PostAsJsonAsync("/api/payables/fiscal-document/upload-url",
            new { fileName = "nota.xml" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Storage.PresignNotSupported", (await ReadJson(res)).GetProperty("title").GetString());
    }
}
