using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Finance.Application.FiscalDocuments;
using Ulbra.One.IntegrationTests;

namespace Ulbra.One.IntegrationTests.Finance;

[Collection("Postgres")]
public class FiscalDocumentEndpointsTests(PostgresFixture fixture)
{
    // Issuer CNPJ is interpolated per seed (vendors are globally unique; the seeded vendor must match it).
    private static string NfeXml(string issuerCnpj) => $"""
<?xml version="1.0" encoding="UTF-8"?>
<nfeProc xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00">
  <NFe>
    <infNFe Id="NFe12345678901234567890123456789012345678901234" versao="4.00">
      <ide><nNF>1234</nNF><serie>1</serie><dhEmi>2026-06-16T14:30:00-03:00</dhEmi></ide>
      <emit><CNPJ>{issuerCnpj}</CNPJ><xNome>Codeforge Desenvolvimento de Software LTDA</xNome></emit>
      <dest><CNPJ>11444777000161</CNPJ><xNome>Industria Mecanica Sul LTDA</xNome></dest>
      <total><ICMSTot><vNF>18000.00</vNF></ICMSTot></total>
    </infNFe>
  </NFe>
</nfeProc>
""";

    private static string NfseXml(string issuerCnpj) => $"""
<?xml version="1.0" encoding="UTF-8"?>
<CompNfse xmlns="http://www.abrasf.org.br/nfse.xsd">
  <Nfse versao="2.04">
    <InfNfse Id="NFSE00000001234">
      <Numero>00000001234</Numero>
      <DataEmissao>2026-06-16T14:32:18</DataEmissao>
      <Servico><Valores><ValorServicos>18000.00</ValorServicos></Valores></Servico>
      <PrestadorServico>
        <IdentificacaoPrestador><CpfCnpj><Cnpj>{issuerCnpj}</Cnpj></CpfCnpj></IdentificacaoPrestador>
        <RazaoSocial>Codeforge Desenvolvimento de Software LTDA</RazaoSocial>
      </PrestadorServico>
      <TomadorServico>
        <IdentificacaoTomador><CpfCnpj><Cnpj>11444777000161</Cnpj></CpfCnpj></IdentificacaoTomador>
      </TomadorServico>
    </InfNfse>
  </Nfse>
</CompNfse>
""";

    // NFS-e national standard (SEFIN). 50-digit key in infNFSe/@Id ("NFS" prefix); emit = provider
    // with the same CNPJ as the seeded vendor; TotalAmount = gross vServ.
    private const string NfseNacionalAccessKey = "12345678901234567890123456789012345678901234567890";
    private static string NfseNacionalXml(string issuerCnpj) => $"""
<?xml version="1.0" encoding="UTF-8"?>
<NFSe versao="1.01" xmlns="http://www.sped.fazenda.gov.br/nfse">
  <infNFSe Id="NFS12345678901234567890123456789012345678901234567890">
    <nNFSe>60063</nNFSe>
    <dhProc>2026-06-15T18:50:53-03:00</dhProc>
    <emit><CNPJ>{issuerCnpj}</CNPJ><xNome>Codeforge Desenvolvimento de Software LTDA</xNome></emit>
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
        </valores>
      </infDPS>
    </DPS>
  </infNFSe>
</NFSe>
""";

    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private sealed record Seed(string CompanyId, string CostCenterId, string VendorId, string IssuerDocument);

    private static async Task<Seed> SeedAsync(HttpClient client)
    {
        var cnpj = TestDocuments.RandomCnpj();
        var companyRes = await client.PostAsJsonAsync("/api/companies", new { legalName = "ULBRA Teste", cnpj });
        companyRes.EnsureSuccessStatusCode();
        var companyId = (await ReadJson(companyRes)).GetProperty("id").GetString()!;

        var ccRes = await client.PostAsJsonAsync("/api/cost-centers",
            new { companyId, code = "CC1", name = "TI", annualBudget = 1_000_000m, autoApprovalLimit = 0m, unitIds = Array.Empty<string>() });
        ccRes.EnsureSuccessStatusCode();
        var costCenterId = (await ReadJson(ccRes)).GetProperty("id").GetString()!;

        // Vendor whose normalized document equals the issuer CNPJ in the sample XMLs (generated per seed).
        var issuer = TestCnpj.New();
        var vendorRes = await client.PostAsJsonAsync("/api/vendors", new
        {
            companyId,
            personType = 1,
            name = "Codeforge Desenvolvimento de Software LTDA",
            tradeName = "Codeforge",
            document = issuer,
            email = "fiscal@codeforge.com.br",
            phone = "5133451200",
            address = (object?)null,
            paymentTerms = "30 dias",
            bankInfo = (string?)null,
        });
        vendorRes.EnsureSuccessStatusCode();
        var vendorId = (await ReadJson(vendorRes)).GetProperty("id").GetString()!;

        return new Seed(companyId, costCenterId, vendorId, issuer);
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

    private static Guid CompanyGuid(string companyId) => CompanyId.Parse(companyId, null).Value;

    /// <summary>A well-formed key in the company's namespace pointing at a blob that was never seeded (AEL-764).</summary>
    private static string MissingScopedKey(string companyId, string leaf) =>
        FiscalDocumentKey.PrefixFor(CompanyGuid(companyId)) + leaf;

    /// <summary>Seeds a blob under the company's fiscal-document namespace and returns the key (AEL-764).</summary>
    private static async Task<string> SeedBlobAsync(ApiFactory factory, string companyId, string xml, string fileName)
    {
        using var scope = factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
        var bytes = Encoding.UTF8.GetBytes(xml);
        using var stream = new MemoryStream(bytes);
        return await storage.SaveAsync(fileName, stream, FiscalDocumentKey.PrefixFor(CompanyGuid(companyId)));
    }

    [Fact]
    public async Task Parse_Nfse_Returns_Service_Fields_And_Matches_Vendor()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var key = await SeedBlobAsync(factory, seed.CompanyId, NfseXml(seed.IssuerDocument), "nfse.xml");

        var res = await client.PostAsJsonAsync(
            $"/api/payables/fiscal-document/parse?companyId={seed.CompanyId}",
            new { key, fileName = "nfse.xml" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await ReadJson(res);
        Assert.Equal("NfseXml", body.GetProperty("format").GetString());
        Assert.Equal("00000001234", body.GetProperty("documentNumber").GetString());
        Assert.Equal(18000.00m, body.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(seed.IssuerDocument, body.GetProperty("issuerDocument").GetString());
        Assert.Equal(seed.VendorId, body.GetProperty("matchedVendorId").GetString());
    }

    [Fact]
    public async Task Parse_Nfe_Returns_Product_Fields_With_FiscalSummary()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var key = await SeedBlobAsync(factory, seed.CompanyId, NfeXml(seed.IssuerDocument), "nfe.xml");

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
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var key = await SeedBlobAsync(factory, seed.CompanyId, NfseNacionalXml(seed.IssuerDocument), "nfse-nacional.xml");

        var res = await client.PostAsJsonAsync(
            $"/api/payables/fiscal-document/parse?companyId={seed.CompanyId}",
            new { key, fileName = "nfse-nacional.xml" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await ReadJson(res);
        Assert.Equal("NfseXml", body.GetProperty("format").GetString());
        Assert.Equal("60063", body.GetProperty("documentNumber").GetString());
        Assert.Equal(18000.00m, body.GetProperty("totalAmount").GetDecimal());   // bruto (vServ)
        Assert.Equal(seed.IssuerDocument, body.GetProperty("issuerDocument").GetString());
        Assert.Equal(seed.VendorId, body.GetProperty("matchedVendorId").GetString());
    }

    [Fact]
    public async Task Upload_NfseNacional_Persists_50Digit_AccessKey()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var payableId = await CreatePayableAsync(client, seed);

        var bytes = Encoding.UTF8.GetBytes(NfseNacionalXml(seed.IssuerDocument));
        var key = await SeedBlobAsync(factory, seed.CompanyId, NfseNacionalXml(seed.IssuerDocument), "nfse-nacional.xml");
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
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);

        // Scoped key, but no blob seeded → storage miss → 400.
        var res = await client.PostAsJsonAsync(
            $"/api/payables/fiscal-document/parse?companyId={seed.CompanyId}",
            new { key = MissingScopedKey(seed.CompanyId, "nonexistent-key.xml"), fileName = "x.xml" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("fiscal_document.not_found", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Parse_UnknownXmlNamespace_Returns_400_UnsupportedFormat()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var key = await SeedBlobAsync(factory, seed.CompanyId, """<?xml version="1.0"?><Foo xmlns="urn:unknown"><Bar/></Foo>""", "x.xml");

        var res = await client.PostAsJsonAsync(
            $"/api/payables/fiscal-document/parse?companyId={seed.CompanyId}",
            new { key, fileName = "x.xml" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("fiscal_document.unsupported_format", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Upload_Persists_Document_And_Replaces_On_Reupload()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var payableId = await CreatePayableAsync(client, seed);

        var nfseBytes = Encoding.UTF8.GetBytes(NfseXml(seed.IssuerDocument));
        var key1 = await SeedBlobAsync(factory, seed.CompanyId, NfseXml(seed.IssuerDocument), "nfse.xml");
        var res = await client.PostAsJsonAsync(
            $"/api/payables/{payableId}/fiscal-document",
            new { key = key1, fileName = "nfse.xml", contentType = "application/xml", sizeBytes = nfseBytes.Length });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var body = await ReadJson(res);
        Assert.Equal("NfseXml", body.GetProperty("format").GetString());
        Assert.Equal("00000001234", body.GetProperty("documentNumber").GetString());

        // Re-upload with a NF-e replaces the existing one (unique per payable) — no 409.
        var nfeBytes = Encoding.UTF8.GetBytes(NfeXml(seed.IssuerDocument));
        var key2 = await SeedBlobAsync(factory, seed.CompanyId, NfeXml(seed.IssuerDocument), "nfe.xml");
        var res2 = await client.PostAsJsonAsync(
            $"/api/payables/{payableId}/fiscal-document",
            new { key = key2, fileName = "nfe.xml", contentType = "application/xml", sizeBytes = nfeBytes.Length });
        Assert.Equal(HttpStatusCode.Created, res2.StatusCode);
        Assert.Equal("NfeXml", (await ReadJson(res2)).GetProperty("format").GetString());
    }

    [Fact]
    public async Task Upload_Sets_Service_Nature_From_Document_Format()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var payableId = await CreatePayableAsync(client, seed);

        // NFS-e (service) → Type = Service. This — not the LC116 subitem — is the withholding trigger;
        // note this ABRASF sample carries no ItemListaServico, yet the payable is still marked a service.
        var nfseBytes = Encoding.UTF8.GetBytes(NfseXml(seed.IssuerDocument));
        var nfseKey = await SeedBlobAsync(factory, seed.CompanyId, NfseXml(seed.IssuerDocument), "nfse.xml");
        var up1 = await client.PostAsJsonAsync($"/api/payables/{payableId}/fiscal-document",
            new { key = nfseKey, fileName = "nfse.xml", contentType = "application/xml", sizeBytes = nfseBytes.Length });
        Assert.Equal(HttpStatusCode.Created, up1.StatusCode);

        var afterNfse = await ReadJson(await client.GetAsync($"/api/payables/{payableId}"));
        Assert.Equal("Service", afterNfse.GetProperty("type").GetString());

        // Re-upload a NF-e (product) → Type = Product → no source withholding.
        var nfeBytes = Encoding.UTF8.GetBytes(NfeXml(seed.IssuerDocument));
        var nfeKey = await SeedBlobAsync(factory, seed.CompanyId, NfeXml(seed.IssuerDocument), "nfe.xml");
        var up2 = await client.PostAsJsonAsync($"/api/payables/{payableId}/fiscal-document",
            new { key = nfeKey, fileName = "nfe.xml", contentType = "application/xml", sizeBytes = nfeBytes.Length });
        Assert.Equal(HttpStatusCode.Created, up2.StatusCode);

        var afterNfe = await ReadJson(await client.GetAsync($"/api/payables/{payableId}"));
        Assert.Equal("Product", afterNfe.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Upload_To_NonExistent_Payable_Returns_404()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        // The payable check precedes the key check, so the key is immaterial here.
        var key = $"fiscal/{Guid.NewGuid():N}/nfse.xml";

        var res = await client.PostAsJsonAsync(
            $"/api/payables/payable_{Guid.NewGuid():N}/fiscal-document",
            new { key, fileName = "nfse.xml", contentType = "application/xml", sizeBytes = 1L });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("payable.not_found", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Upload_With_Missing_Key_Returns_400_NotFound()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);
        var payableId = await CreatePayableAsync(client, seed);

        // Scoped key, but no blob seeded → storage miss → 400.
        var res = await client.PostAsJsonAsync(
            $"/api/payables/{payableId}/fiscal-document",
            new { key = MissingScopedKey(seed.CompanyId, "ghost-key.xml"), fileName = "nfse.xml", contentType = "application/xml", sizeBytes = 1L });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("fiscal_document.not_found", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task Parse_BlobOverLimit_Returns_400_TooLarge()
    {
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();
        var seed = await SeedAsync(client);

        // Seed a blob just over 25 MB, scoped to the company.
        using var scope = factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
        using var ms = new MemoryStream(new byte[26 * 1024 * 1024]);
        var key = await storage.SaveAsync("big.xml", ms, FiscalDocumentKey.PrefixFor(CompanyGuid(seed.CompanyId)));

        var res = await client.PostAsJsonAsync(
            $"/api/payables/fiscal-document/parse?companyId={seed.CompanyId}",
            new { key, fileName = "big.xml" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("fiscal_document.too_large", (await ReadJson(res)).GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task UploadUrl_Returns_400_PresignNotSupported_On_Local_Storage()
    {
        // In the test environment, LocalFileStorageService is used (no AzureBlob provider configured),
        // which throws NotSupportedException. The endpoint catches this and returns a 400 with
        // Storage.PresignNotSupported so the frontend can fall back to direct multipart upload.
        var factory = fixture.Api;
        var client = await factory.CreateAdminClientAsync();

        // AEL-764: companyId is now required (admin bypasses the CanAccess check).
        var companyId = new CompanyId(Guid.NewGuid());
        var res = await client.PostAsJsonAsync($"/api/payables/fiscal-document/upload-url?companyId={companyId}",
            new { fileName = "nota.xml" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("storage.presign_not_supported", (await ReadJson(res)).GetProperty("error_code").GetString());
    }
}
