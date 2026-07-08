using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Ulbra.One.Common.Domain.Validation;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.FiscalDocuments;
using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Finance.Infrastructure.FiscalDocuments;

/// <summary>
/// Parses NFS-e in the national standard (SEFIN Nacional / Sistema Nacional NFS-e). Namespace:
/// sped.fazenda.gov.br/nfse. Structure: <c>NFSe</c> → <c>infNFSe</c> (authorized note, with <c>emit</c>)
/// → <c>DPS</c> → <c>infDPS</c> (the declaration: <c>prest</c>/<c>toma</c>/<c>serv</c>/<c>valores</c>).
/// Detection is by namespace only (the root is the <c>NFSe</c> envelope, but a bare <c>DPS</c> shares
/// the namespace). The 50-digit access key sits in <c>infNFSe/@Id</c> with an <c>NFS</c> prefix.
/// </summary>
/// <remarks>
/// The layout repeats <c>valores</c> across branches (under infNFSe, infDPS AND IBSCBS), so it is
/// reached by an explicit parent chain — never <c>Descendants(Ns + "valores")</c>, which would grab
/// the wrong block.
/// </remarks>
public sealed class NfseNacionalXmlParser : IFiscalXmlParser
{
    private static readonly XNamespace Ns = "http://www.sped.fazenda.gov.br/nfse";

    public bool CanParse(XDocument document) =>
        document.Root is not null && document.Root.Name.Namespace == Ns;

    public ParsedFiscalDocument Parse(XDocument document)
    {
        // infNFSe = authorized note (SEFIN-computed echo: emit, nNFSe, vISSQN/vLiq).
        // infDPS   = the taxpayer declaration (prest/toma/serv/valores/trib).
        var infNFSe = document.Descendants(Ns + "infNFSe").FirstOrDefault();
        var infDPS = document.Descendants(Ns + "infDPS").FirstOrDefault();
        if (infNFSe is null && infDPS is null)
            throw new InvalidOperationException("NFS-e Nacional: nem infNFSe nem infDPS encontrados.");

        var emit = infNFSe?.Element(Ns + "emit");
        var toma = infDPS?.Element(Ns + "toma");
        var serv = infDPS?.Element(Ns + "serv");
        var cServ = serv?.Element(Ns + "cServ");
        var dpsValores = infDPS?.Element(Ns + "valores");
        var nfseValores = infNFSe?.Element(Ns + "valores");

        var documentNumber = infNFSe?.Element(Ns + "nNFSe")?.Value;
        var issueDate = FiscalXml.ParseDate(
            infDPS?.Element(Ns + "dhEmi")?.Value ?? infNFSe?.Element(Ns + "dhProc")?.Value);

        // TotalAmount = gross service value (vServ); vLiq is only a fallback (it is not a reliable net field).
        var serviceAmount = FiscalXml.ParseDecimal(
            dpsValores?.Element(Ns + "vServPrest")?.Element(Ns + "vServ")?.Value);
        var totalAmount = serviceAmount ?? FiscalXml.ParseDecimal(nfseValores?.Element(Ns + "vLiq")?.Value);

        var issuerDocument = FiscalXml.NullIfEmpty(DocumentValidator.Normalize(
            emit?.Element(Ns + "CNPJ")?.Value ?? emit?.Element(Ns + "CPF")?.Value));
        var issuerName = emit?.Element(Ns + "xNome")?.Value;
        var issuerPhone = FiscalXml.NullIfEmpty(emit?.Element(Ns + "fone")?.Value);
        var issuerAddress = BuildIssuerAddress(emit?.Element(Ns + "enderNac"));

        var recipientDocument = FiscalXml.NullIfEmpty(DocumentValidator.Normalize(
            toma?.Element(Ns + "CNPJ")?.Value ?? toma?.Element(Ns + "CPF")?.Value));

        // DPS is the national analog of the RPS (series + declaration number).
        var rpsNumber = infDPS?.Element(Ns + "nDPS")?.Value;
        var rpsSeries = infDPS?.Element(Ns + "serie")?.Value;
        var serviceDescription = cServ?.Element(Ns + "xDescServ")?.Value;
        var tribNacCode = cServ?.Element(Ns + "cTribNac")?.Value;
        var nbsCode = cServ?.Element(Ns + "cNBS")?.Value;
        // LC 116 subitem derived from cTribNac (first 4 digits = item.subitem), canonicalized via the shared Lc116
        // normalizer — the same form the ABRASF parser produces, so the withholding rules match either standard.
        var lc116Subitem = FiscalXml.Lc116FromCTribNac(tribNacCode);

        // The national key is 50 digits, prefixed with "NFS" in infNFSe/@Id (vs "NFe"/44 for NF-e).
        var idAttribute = infNFSe?.Attribute("Id")?.Value ?? string.Empty;
        var accessKey = FiscalXml.NullIfEmpty(
            idAttribute.StartsWith("NFS", StringComparison.OrdinalIgnoreCase) ? idAttribute[3..] : idAttribute);

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
            ["cTribNac"] = tribNacCode,
            ["cNBS"] = nbsCode,
            ["lc116Subitem"] = lc116Subitem,
        });

        return new ParsedFiscalDocument(FiscalDocumentFormat.NfseXml, accessKey, documentNumber,
            issueDate, totalAmount, issuerDocument, issuerName, recipientDocument, rawExtraction, [],
            RpsNumber: FiscalXml.NullIfEmpty(rpsNumber), RpsSeries: FiscalXml.NullIfEmpty(rpsSeries),
            ServiceDescription: FiscalXml.NullIfEmpty(serviceDescription),
            Lc116Subitem: lc116Subitem,
            IssuerPhone: issuerPhone, IssuerAddress: issuerAddress);
    }

    // Maps the national enderNac to ParsedAddress. UF is present; the city comes only as an IBGE code
    // (cMun), so City is left null. Returns null when the block is absent or fully empty.
    private static ParsedAddress? BuildIssuerAddress(XElement? enderNac)
    {
        if (enderNac is null) return null;
        var address = new ParsedAddress(
            Cep: FiscalXml.NullIfEmpty(enderNac.Element(Ns + "CEP")?.Value),
            Street: FiscalXml.NullIfEmpty(enderNac.Element(Ns + "xLgr")?.Value),
            Number: FiscalXml.NullIfEmpty(enderNac.Element(Ns + "nro")?.Value),
            Neighborhood: FiscalXml.NullIfEmpty(enderNac.Element(Ns + "xBairro")?.Value),
            City: null,
            State: FiscalXml.NullIfEmpty(enderNac.Element(Ns + "UF")?.Value));
        var empty = address is { Cep: null, Street: null, Number: null, Neighborhood: null, State: null };
        return empty ? null : address;
    }
}
