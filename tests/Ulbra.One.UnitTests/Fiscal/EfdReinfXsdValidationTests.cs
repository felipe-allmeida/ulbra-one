using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using Ulbra.One.Fiscal.Domain.EfdReinf;
using Xunit;

namespace Ulbra.One.UnitTests.Fiscal;

/// <summary>Validates the generated EFD-Reinf R-4020 XML against the OFFICIAL XSD
/// (<c>evt4020PagtoBeneficiarioPJ v2_01_02</c>, vendored under Fiscal/schemas). The lote wraps one signed event
/// document per (estabelecimento × beneficiário): <c>&lt;Reinf xmlns=evt4020&gt;…&lt;/Reinf&gt;</c>. We validate
/// each event subtree; the only tolerated gap is the mandatory <c>ds:Signature</c>, which is added at signing —
/// a deferred (transmissão) concern, out of scope here.</summary>
public class EfdReinfXsdValidationTests
{
    private const string EvtNs = "http://www.reinf.esocial.gov.br/schemas/evt4020PagtoBeneficiarioPJ/v2_01_02";

    private static ReinfR4020Input SampleInput() => new(
        "2026-07", 2, new ReinfTaxpayer("99999999", "Empresa A"),
        [
            // Dois beneficiários no mesmo estabelecimento → dois eventos; o segundo com duas naturezas.
            new ReinfR4020Payment("99999999000191", "11222333000181", "15009",
                new DateOnly(2026, 7, 15), 1000m, 1000m, 15m, 1000m, 10m, 1000m, 30m, 1000m, 6.5m),
            new ReinfR4020Payment("99999999000191", "44555666000177", "15009",
                new DateOnly(2026, 7, 20), 2000m, 2000m, 30m, 0m, 0m, 0m, 0m, 0m, 0m),
            new ReinfR4020Payment("99999999000191", "44555666000177", "17010",
                new DateOnly(2026, 7, 22), 500m, 500m, 7.5m, 0m, 0m, 0m, 0m, 0m, 0m),
        ]);

    [Fact]
    public void Generated_r4020_event_conforms_to_official_xsd_except_deferred_signature()
    {
        var xml = EfdReinfXmlBuilder.Build(SampleInput());
        var doc = XDocument.Parse(xml);

        var events = doc.Descendants(XName.Get("Reinf", EvtNs)).ToList();
        Assert.NotEmpty(events); // one event document per (estab × beneficiário)

        var schemas = LoadSchemas();
        foreach (var evt in events)
        {
            var errors = Validate(evt, schemas);
            // ds:Signature is required by the XSD but only added at signing (deferred). Tolerate ONLY that.
            var blocking = errors
                .Where(e => !e.Contains("Signature", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.True(blocking.Count == 0, "Erros de XSD (fora da assinatura):\n" + string.Join("\n", blocking));
        }
    }

    private static XmlSchemaSet LoadSchemas()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Fiscal", "schemas");
        // XmlResolver is null by default (blocks external refs) — enable it so the evt4020 XSD's relative
        // <xs:import> of xmldsig-core-schema.xsd resolves; otherwise ds:Signature stays undeclared.
        var set = new XmlSchemaSet { XmlResolver = new XmlUrlResolver() };
        set.Add(EvtNs, Path.Combine(dir, "R-4020-evt4020PagtoBeneficiarioPJ-v2_01_02_B.xsd"));
        set.Compile();
        return set;
    }

    private static List<string> Validate(XElement element, XmlSchemaSet schemas)
    {
        var errors = new List<string>();
        var settings = new XmlReaderSettings { ValidationType = ValidationType.Schema };
        settings.Schemas.Add(schemas);
        settings.ValidationEventHandler += (_, e) => errors.Add(e.Message);
        using var reader = XmlReader.Create(new StringReader(element.ToString()), settings);
        while (reader.Read()) { }
        return errors;
    }
}
