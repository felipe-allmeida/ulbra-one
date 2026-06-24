using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Ulbra.One.Common.Domain.Validation;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.FiscalDocuments;
using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Finance.Infrastructure.FiscalDocuments;

/// <summary>
/// Parses NF-e modelo 55 (produto/mercadoria) — the single national SEFAZ layout. Accepts both the
/// processed envelope (<c>nfeProc</c>) and a bare <c>NFe</c>. Namespace: portalfiscal.inf.br/nfe.
/// </summary>
public sealed class NfeM55XmlParser : IFiscalXmlParser
{
    private static readonly XNamespace Ns = "http://www.portalfiscal.inf.br/nfe";

    public bool CanParse(XDocument document)
    {
        var root = document.Root;
        return root is not null && root.Name.Namespace == Ns && root.Name.LocalName is "nfeProc" or "NFe";
    }

    public ParsedFiscalDocument Parse(XDocument document)
    {
        // NF-e element groups (cryptic XML tags mapped to readable names): ide=identification,
        // emit=issuer, dest=recipient, total/ICMSTot=totals, cobr=billing, pag=payment.
        var nfeInfo = document.Descendants(Ns + "infNFe").FirstOrDefault()
            ?? throw new InvalidOperationException("NF-e infNFe element not found.");
        var identification = nfeInfo.Element(Ns + "ide");
        var issuer = nfeInfo.Element(Ns + "emit");
        var recipient = nfeInfo.Element(Ns + "dest");
        var totals = nfeInfo.Element(Ns + "total")?.Element(Ns + "ICMSTot");

        // The infNFe "Id" attribute is the 44-digit access key prefixed with the literal "NFe".
        var idAttribute = nfeInfo.Attribute("Id")?.Value ?? string.Empty;
        var accessKey = FiscalXml.NullIfEmpty(
            idAttribute.StartsWith("NFe", StringComparison.OrdinalIgnoreCase) ? idAttribute[3..] : idAttribute);

        var documentNumber = identification?.Element(Ns + "nNF")?.Value;
        var seriesNumber = identification?.Element(Ns + "serie")?.Value;
        var issueDate = FiscalXml.ParseDate(
            identification?.Element(Ns + "dhEmi")?.Value ?? identification?.Element(Ns + "dEmi")?.Value);
        var totalAmount = FiscalXml.ParseDecimal(totals?.Element(Ns + "vNF")?.Value);
        var issuerDocument = FiscalXml.NullIfEmpty(
            DocumentValidator.Normalize(issuer?.Element(Ns + "CNPJ")?.Value ?? issuer?.Element(Ns + "CPF")?.Value));
        var issuerName = issuer?.Element(Ns + "xNome")?.Value;
        var issuerPhone = FiscalXml.NullIfEmpty(issuer?.Element(Ns + "enderEmit")?.Element(Ns + "fone")?.Value);
        var issuerAddress = BuildIssuerAddress(issuer?.Element(Ns + "enderEmit"));
        var recipientDocument = FiscalXml.NullIfEmpty(
            DocumentValidator.Normalize(recipient?.Element(Ns + "CNPJ")?.Value ?? recipient?.Element(Ns + "CPF")?.Value));

        // Billing (cobr): each <dup> (0..120) is one duplicata — due date (dVenc) + amount (vDup) → installment.
        var billing = nfeInfo.Element(Ns + "cobr");
        var installments = new List<ParsedInstallment>();
        var duplicatesRaw = new List<object>();
        foreach (var duplicate in billing?.Elements(Ns + "dup") ?? [])
        {
            var dueDateText = duplicate.Element(Ns + "dVenc")?.Value;
            var amountText = duplicate.Element(Ns + "vDup")?.Value;
            duplicatesRaw.Add(new { number = duplicate.Element(Ns + "nDup")?.Value, dueDate = dueDateText, amount = amountText });
            if (FiscalXml.ParseDate(dueDateText) is { } dueDate && FiscalXml.ParseDecimal(amountText) is { } amount && amount > 0)
                installments.Add(new ParsedInstallment(dueDate, amount));
        }

        // Item descriptions (det/prod/xProd) joined into one free-text description — the NF-e analog of the
        // NFS-e "discriminação"; used to pre-fill the payable description (à NFS-e já preenche por serviceDescription).
        var productDescriptions = nfeInfo.Elements(Ns + "det")
            .Select(item => item.Element(Ns + "prod")?.Element(Ns + "xProd")?.Value?.Trim())
            .Where(text => !string.IsNullOrEmpty(text))
            .ToList();
        var description = FiscalXml.NullIfEmpty(string.Join("; ", productDescriptions));

        // Fiscal signals: distinct CFOPs and NCMs across all items + ICMSTot tax totals.
        var cfops = nfeInfo.Elements(Ns + "det")
            .Select(i => i.Element(Ns + "prod")?.Element(Ns + "CFOP")?.Value?.Trim())
            .Where(c => !string.IsNullOrEmpty(c)).Select(c => c!).Distinct().ToList();
        var ncms = nfeInfo.Elements(Ns + "det")
            .Select(i => i.Element(Ns + "prod")?.Element(Ns + "NCM")?.Value?.Trim())
            .Where(c => !string.IsNullOrEmpty(c)).Select(c => c!).Distinct().ToList();
        var fiscal = new FiscalSummary(
            cfops, ncms,
            FiscalXml.ParseDecimal(totals?.Element(Ns + "vICMS")?.Value),
            FiscalXml.ParseDecimal(totals?.Element(Ns + "vIPI")?.Value),
            FiscalXml.ParseDecimal(totals?.Element(Ns + "vPIS")?.Value),
            FiscalXml.ParseDecimal(totals?.Element(Ns + "vCOFINS")?.Value),
            FiscalXml.ParseDecimal(totals?.Element(Ns + "vTotTrib")?.Value));

        var invoice = billing?.Element(Ns + "fat"); // fatura-mãe (sintética)
        var payment = nfeInfo.Element(Ns + "pag"); // <pag>/<detPag>: forma + valor (parcelado ou único somando vNF)
        var rawExtraction = JsonSerializer.Serialize(new
        {
            accessKey,
            documentNumber,
            seriesNumber,
            issueDate = issueDate?.ToString("O"),
            totalAmount = totalAmount?.ToString(CultureInfo.InvariantCulture),
            issuerDocument,
            issuerName,
            issuerPhone,
            issuerAddress,
            recipientDocument,
            description,
            invoice = invoice is null
                ? null
                : new
                {
                    number = invoice.Element(Ns + "nFat")?.Value,
                    originalAmount = invoice.Element(Ns + "vOrig")?.Value,
                    discountAmount = invoice.Element(Ns + "vDesc")?.Value,
                    netAmount = invoice.Element(Ns + "vLiq")?.Value,
                },
            duplicates = duplicatesRaw,
            payments = payment?.Elements(Ns + "detPag")
                .Select(detail => new { method = detail.Element(Ns + "tPag")?.Value, amount = detail.Element(Ns + "vPag")?.Value })
                .ToList(),
        });

        return new ParsedFiscalDocument(FiscalDocumentFormat.NfeXml, accessKey, documentNumber,
            issueDate, totalAmount, issuerDocument, issuerName, recipientDocument, rawExtraction, installments,
            ServiceDescription: description, IssuerPhone: issuerPhone, IssuerAddress: issuerAddress,
            Fiscal: fiscal);
    }

    // Maps NF-e enderEmit to ParsedAddress; null when the address block is absent or fully empty.
    private static ParsedAddress? BuildIssuerAddress(XElement? enderEmit)
    {
        if (enderEmit is null) return null;
        var address = new ParsedAddress(
            Cep: FiscalXml.NullIfEmpty(enderEmit.Element(Ns + "CEP")?.Value),
            Street: FiscalXml.NullIfEmpty(enderEmit.Element(Ns + "xLgr")?.Value),
            Number: FiscalXml.NullIfEmpty(enderEmit.Element(Ns + "nro")?.Value),
            Neighborhood: FiscalXml.NullIfEmpty(enderEmit.Element(Ns + "xBairro")?.Value),
            City: FiscalXml.NullIfEmpty(enderEmit.Element(Ns + "xMun")?.Value),
            State: FiscalXml.NullIfEmpty(enderEmit.Element(Ns + "UF")?.Value));
        var empty = address is { Cep: null, Street: null, Number: null, Neighborhood: null, City: null, State: null };
        return empty ? null : address;
    }
}
