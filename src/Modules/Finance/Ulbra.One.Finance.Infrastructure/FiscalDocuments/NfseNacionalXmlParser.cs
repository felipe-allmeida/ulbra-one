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
/// The layout repeats element local-names across branches (<c>valores</c> under infNFSe, infDPS AND
/// IBSCBS; <c>vBC</c> ×3; <c>CST</c> ×2), so every field is reached by an explicit parent chain —
/// never <c>Descendants(Ns + "valores")</c>, which would grab the wrong block.
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

        // TotalAmount = gross service value (vServ). The net is gross − Σ withholdings, derived
        // downstream from Withholdings; vLiq is only a fallback (it is not a reliable net field).
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

        // DPS is the national analog of the RPS (série + número da declaração).
        var rpsNumber = infDPS?.Element(Ns + "nDPS")?.Value;
        var rpsSeries = infDPS?.Element(Ns + "serie")?.Value;
        var serviceDescription = cServ?.Element(Ns + "xDescServ")?.Value;
        var tribNacCode = cServ?.Element(Ns + "cTribNac")?.Value;
        var nbsCode = cServ?.Element(Ns + "cNBS")?.Value;

        // The national key is 50 digits, prefixed with "NFS" in infNFSe/@Id (vs "NFe"/44 for NF-e).
        var idAttribute = infNFSe?.Attribute("Id")?.Value ?? string.Empty;
        var accessKey = FiscalXml.NullIfEmpty(
            idAttribute.StartsWith("NFS", StringComparison.OrdinalIgnoreCase) ? idAttribute[3..] : idAttribute);

        var withholdings = ExtractWithholdings(infDPS, nfseValores, totalAmount, out var withholdingNotes);

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
            ["withholdingNotes"] = withholdingNotes,
        });

        return new ParsedFiscalDocument(FiscalDocumentFormat.NfseXml, accessKey, documentNumber,
            issueDate, totalAmount, issuerDocument, issuerName, recipientDocument, rawExtraction, [],
            RpsNumber: FiscalXml.NullIfEmpty(rpsNumber), RpsSeries: FiscalXml.NullIfEmpty(rpsSeries),
            ServiceDescription: FiscalXml.NullIfEmpty(serviceDescription),
            IssuerPhone: issuerPhone, IssuerAddress: issuerAddress, Withholdings: withholdings);
    }

    // Reads the retention indicators declared in infDPS/valores/trib. ISS amount/base/rate live in
    // infNFSe/valores (SEFIN-computed); the retention type flag lives in infDPS/.../tribMun.
    private static IReadOnlyList<ParsedWithholding> ExtractWithholdings(
        XElement? infDPS, XElement? nfseValores, decimal? grossAmount, out string? notes)
    {
        notes = null;
        var trib = infDPS?.Element(Ns + "valores")?.Element(Ns + "trib");
        if (trib is null) return [];

        var fallbackBase = grossAmount ?? 0m;
        var result = new List<ParsedWithholding>();

        // ISS — withheld only when tpRetISSQN is 2 (tomador) or 3 (intermediário); 1 = não retido.
        var tribMun = trib.Element(Ns + "tribMun");
        var tpRetIss = tribMun?.Element(Ns + "tpRetISSQN")?.Value;
        if (tpRetIss is "2" or "3")
        {
            var issAmount = FiscalXml.ParseDecimal(
                nfseValores?.Element(Ns + "vISSQN")?.Value ?? tribMun?.Element(Ns + "vISSQN")?.Value);
            if (issAmount is > 0)
            {
                var issBase = FiscalXml.ParseDecimal(nfseValores?.Element(Ns + "vBC")?.Value) ?? fallbackBase;
                var issRate = FiscalXml.ParseDecimal(nfseValores?.Element(Ns + "pAliqAplic")?.Value);
                result.Add(new ParsedWithholding(WithholdingTaxType.Iss, issBase, issRate, issAmount.Value));
            }
            if (tpRetIss == "3")
                notes = "ISS retido pelo intermediário (tpRetISSQN=3), não pelo tomador.";
        }

        // Federal standalone retentions (retidos pelo tomador). vPis/vCofins under piscofins are the
        // provider's own assessment (NOT retention) and are deliberately ignored.
        var tribFed = trib.Element(Ns + "tribFed");
        AddIfPositive(result, tribFed?.Element(Ns + "vRetIRRF")?.Value, WithholdingTaxType.Irrf, fallbackBase);
        AddIfPositive(result, tribFed?.Element(Ns + "vRetCP")?.Value, WithholdingTaxType.Inss, fallbackBase);
        var csll = FiscalXml.ParseDecimal(tribFed?.Element(Ns + "vRetCSLL")?.Value);
        if (csll is > 0)
        {
            result.Add(new ParsedWithholding(WithholdingTaxType.Csll, fallbackBase, null, csll.Value));
            // Post-NT007 vRetCSLL is consolidated (PIS+COFINS+CSLL); recorded whole under Csll here.
            notes = string.Join(" ", new[] { notes, "vRetCSLL pós-NT007 é consolidado (PIS+COFINS+CSLL)." }
                .Where(s => !string.IsNullOrEmpty(s)));
        }

        return result;
    }

    private static void AddIfPositive(
        List<ParsedWithholding> list, string? rawValue, WithholdingTaxType type, decimal baseAmount)
    {
        var amount = FiscalXml.ParseDecimal(rawValue);
        if (amount is > 0)
            list.Add(new ParsedWithholding(type, baseAmount, null, amount.Value));
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
