using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Ulbra.One.Common.Domain.Validation;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.FiscalDocuments;
using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Finance.Infrastructure.FiscalDocuments;

/// <summary>
/// Parses NFS-e in the ABRASF national standard (serviço). Namespace: abrasf.org.br/nfse.xsd. Detection
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
        // TotalAmount = gross (ValorServicos). The net is gross − Σ withholdings, derived downstream;
        // ValorLiquidoNfse is only a fallback. (Changed from liquid-first — see AEL-646.)
        var totalAmount = FiscalXml.ParseDecimal(
            serviceAmounts?.Element(Ns + "ValorServicos")?.Value ?? serviceAmounts?.Element(Ns + "ValorLiquidoNfse")?.Value);
        var withholdings = ExtractWithholdings(serviceAmounts, totalAmount);

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
            ["withholdingCount"] = withholdings.Count.ToString(CultureInfo.InvariantCulture),
        });

        return new ParsedFiscalDocument(FiscalDocumentFormat.NfseXml, null, documentNumber,
            issueDate, totalAmount, issuerDocument, issuerName, recipientDocument, rawExtraction, [],
            RpsNumber: FiscalXml.NullIfEmpty(rpsNumber), RpsSeries: FiscalXml.NullIfEmpty(rpsSeries),
            ServiceDescription: FiscalXml.NullIfEmpty(serviceDescription), CnaeCode: FiscalXml.NullIfEmpty(cnaeCode),
            Withholdings: withholdings);
    }

    // Reads the retention indicators declared in Servico/Valores (retidos pelo tomador). ISS is
    // withheld only when IssRetido = 1; the federal Valor* fields are direct retention amounts.
    private static IReadOnlyList<ParsedWithholding> ExtractWithholdings(XElement? amounts, decimal? grossAmount)
    {
        if (amounts is null) return [];

        var fallbackBase = grossAmount ?? 0m;
        var result = new List<ParsedWithholding>();

        if (amounts.Element(Ns + "IssRetido")?.Value == "1")
        {
            var issAmount = FiscalXml.ParseDecimal(
                amounts.Element(Ns + "ValorIssRetido")?.Value ?? amounts.Element(Ns + "ValorIss")?.Value);
            if (issAmount is > 0)
                result.Add(new ParsedWithholding(WithholdingTaxType.Iss, fallbackBase, null, issAmount.Value));
        }

        AddIfPositive(result, amounts.Element(Ns + "ValorPis")?.Value, WithholdingTaxType.Pis, fallbackBase);
        AddIfPositive(result, amounts.Element(Ns + "ValorCofins")?.Value, WithholdingTaxType.Cofins, fallbackBase);
        AddIfPositive(result, amounts.Element(Ns + "ValorInss")?.Value, WithholdingTaxType.Inss, fallbackBase);
        AddIfPositive(result, amounts.Element(Ns + "ValorIr")?.Value, WithholdingTaxType.Irrf, fallbackBase);
        AddIfPositive(result, amounts.Element(Ns + "ValorCsll")?.Value, WithholdingTaxType.Csll, fallbackBase);
        AddIfPositive(result, amounts.Element(Ns + "OutrasRetencoes")?.Value, WithholdingTaxType.Other, fallbackBase);

        return result;
    }

    private static void AddIfPositive(
        List<ParsedWithholding> list, string? rawValue, WithholdingTaxType type, decimal baseAmount)
    {
        var amount = FiscalXml.ParseDecimal(rawValue);
        if (amount is > 0)
            list.Add(new ParsedWithholding(type, baseAmount, null, amount.Value));
    }
}
