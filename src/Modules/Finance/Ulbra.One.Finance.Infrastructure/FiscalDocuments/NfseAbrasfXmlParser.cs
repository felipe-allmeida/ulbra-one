using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Ulbra.One.Common.Domain.Validation;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.FiscalDocuments;
using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Finance.Infrastructure.FiscalDocuments;

/// <summary>
/// Parses NFS-e in the ABRASF national standard (service). Namespace: abrasf.org.br/nfse.xsd. Detection
/// is by namespace only (the root varies: <c>CompNfse</c>, query responses, etc.); the <c>InfNfse</c>
/// block is located anywhere in the tree.
/// </summary>
public sealed class NfseAbrasfXmlParser : IFiscalXmlParser
{
    private static readonly XNamespace Ns = "http://www.abrasf.org.br/nfse.xsd";

    public bool CanParse(XDocument document) =>
        document.Root is not null && document.Root.Name.Namespace == Ns;

    public ParsedFiscalDocument Parse(XDocument document)
    {
        // NFS-e element groups: PrestadorServico=provider (issuer), TomadorServico=customer (recipient),
        // Servico/Valores=service amounts, IdentificacaoRps=RPS reference.
        var nfseInfo = document.Descendants(Ns + "InfNfse").FirstOrDefault()
            ?? throw new InvalidOperationException("NFS-e InfNfse element not found.");
        var service = nfseInfo.Element(Ns + "Servico");
        var serviceAmounts = service?.Element(Ns + "Valores");
        var provider = nfseInfo.Element(Ns + "PrestadorServico");
        var customer = nfseInfo.Element(Ns + "TomadorServico");

        var documentNumber = nfseInfo.Element(Ns + "Numero")?.Value;
        var issueDate = FiscalXml.ParseDate(nfseInfo.Element(Ns + "DataEmissao")?.Value);
        // TotalAmount = gross service value (ValorServicos); ValorLiquidoNfse is only a fallback.
        // (Changed from liquid-first — see AEL-646.)
        var totalAmount = FiscalXml.ParseDecimal(
            serviceAmounts?.Element(Ns + "ValorServicos")?.Value ?? serviceAmounts?.Element(Ns + "ValorLiquidoNfse")?.Value);

        var providerIdentification = provider?.Element(Ns + "IdentificacaoPrestador");
        var providerCpfCnpj = providerIdentification?.Element(Ns + "CpfCnpj");
        var issuerDocument = FiscalXml.NullIfEmpty(DocumentValidator.Normalize(
            providerCpfCnpj?.Element(Ns + "Cnpj")?.Value
            ?? providerCpfCnpj?.Element(Ns + "Cpf")?.Value
            ?? providerIdentification?.Element(Ns + "Cnpj")?.Value));
        var issuerName = provider?.Element(Ns + "RazaoSocial")?.Value;

        var customerCpfCnpj = customer?.Element(Ns + "IdentificacaoTomador")?.Element(Ns + "CpfCnpj");
        var recipientDocument = FiscalXml.NullIfEmpty(DocumentValidator.Normalize(
            customerCpfCnpj?.Element(Ns + "Cnpj")?.Value ?? customerCpfCnpj?.Element(Ns + "Cpf")?.Value));

        var rpsIdentification = nfseInfo.Element(Ns + "IdentificacaoRps");
        var rpsNumber = rpsIdentification?.Element(Ns + "Numero")?.Value;
        var rpsSeries = rpsIdentification?.Element(Ns + "Serie")?.Value;
        var serviceDescription = service?.Element(Ns + "Discriminacao")?.Value;
        var cnaeCode = service?.Element(Ns + "CodigoCnae")?.Value;
        // NFS-e service/tax signals. ItemListaServico (LC 116 subitem) is the reliable one; the others
        // are optional in ABRASF (best-effort for ISS). Aliquota/IssRetido live under Servico/Valores.
        // Canonicalized (Lc116) so a municipality's padded/undotted form ("01.07" / "0107") matches the same
        // rule keys the national parser produces — the single choke-point for LC 116 across both standards.
        var lc116Subitem = Lc116.Canonical(service?.Element(Ns + "ItemListaServico")?.Value);
        var municipalServiceCode = service?.Element(Ns + "CodigoTributacaoMunicipio")?.Value;
        var serviceMunicipalityIbge = service?.Element(Ns + "CodigoMunicipio")?.Value;
        var invoiceIssRate = FiscalXml.ParseDecimal(
            serviceAmounts?.Element(Ns + "Aliquota")?.Value ?? service?.Element(Ns + "Aliquota")?.Value);
        var issWithheldOnInvoice = ParseSimNao(
            serviceAmounts?.Element(Ns + "IssRetido")?.Value ?? service?.Element(Ns + "IssRetido")?.Value);

        var rawExtraction = JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["documentNumber"] = documentNumber,
            ["issueDate"] = issueDate?.ToString("O"),
            ["totalAmount"] = totalAmount?.ToString(CultureInfo.InvariantCulture),
            ["issuerDocument"] = issuerDocument,
            ["issuerName"] = issuerName,
            ["recipientDocument"] = recipientDocument,
            ["rpsNumber"] = rpsNumber,
            ["rpsSeries"] = rpsSeries,
            ["serviceDescription"] = serviceDescription,
            ["cnaeCode"] = cnaeCode,
            ["lc116Subitem"] = lc116Subitem,
            ["municipalServiceCode"] = municipalServiceCode,
            ["serviceMunicipalityIbge"] = serviceMunicipalityIbge,
            ["invoiceIssRate"] = invoiceIssRate?.ToString(CultureInfo.InvariantCulture),
            ["issWithheldOnInvoice"] = issWithheldOnInvoice?.ToString(),
        });

        return new ParsedFiscalDocument(FiscalDocumentFormat.NfseXml, null, documentNumber,
            issueDate, totalAmount, issuerDocument, issuerName, recipientDocument, rawExtraction, [],
            RpsNumber: FiscalXml.NullIfEmpty(rpsNumber), RpsSeries: FiscalXml.NullIfEmpty(rpsSeries),
            ServiceDescription: FiscalXml.NullIfEmpty(serviceDescription), CnaeCode: FiscalXml.NullIfEmpty(cnaeCode),
            Lc116Subitem: FiscalXml.NullIfEmpty(lc116Subitem),
            MunicipalServiceCode: FiscalXml.NullIfEmpty(municipalServiceCode),
            ServiceMunicipalityIbge: FiscalXml.NullIfEmpty(serviceMunicipalityIbge),
            InvoiceIssRate: invoiceIssRate,
            IssWithheldOnInvoice: issWithheldOnInvoice);
    }

    // ABRASF tcSimNao: 1 = Yes, 2 = No; any other value (or absent) -> null.
    private static bool? ParseSimNao(string? value) => value?.Trim() switch
    {
        "1" => true,
        "2" => false,
        _ => null,
    };
}
