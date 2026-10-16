using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using Ulbra.One.Fiscal.Domain.EfdReinf;
using Xunit;

namespace Ulbra.One.UnitTests.Fiscal;

/// <summary>Validates the generated EFD-Reinf R-2010 XML against the OFFICIAL XSD
/// (<c>evtTomadorServicos v2_01_02</c>, vendored under Fiscal/schemas). The lote wraps one signed event document
/// per (estabelecimento tomador × prestador): <c>&lt;Reinf xmlns=evtTomadorServicos&gt;…&lt;/Reinf&gt;</c>. We
/// validate each event subtree; the only tolerated gap is the mandatory <c>ds:Signature</c>, added at signing — a
/// deferred (transmissão) concern, out of scope here.</summary>
public class EfdReinfR2010XsdValidationTests
{
    private const string EvtNs = "http://www.reinf.esocial.gov.br/schemas/evtTomadorServicos/v2_01_02";

    // One estabelecimento tomador, two prestadores → two events. Prestador A has two NFs at 11% (indCPRB 0);
    // prestador B one NF at 3,5% (indCPRB 1, derived from the effective rate).
    private static ReinfR2010Input SampleInput() => new(
        "2026-07", 2, new ReinfTaxpayer("99999999", "Empresa A"),
        [
            new ReinfR2010Nf("99999999000191", "11222333000181", "1", "1001",
                new DateOnly(2026, 7, 10), 5000m, "100000002", 5000m, 550m),
            new ReinfR2010Nf("99999999000191", "11222333000181", "1", "1002",
                new DateOnly(2026, 7, 18), 3000m, "100000002", 3000m, 330m),
            new ReinfR2010Nf("99999999000191", "44555666000177", "1", "77",
                new DateOnly(2026, 7, 20), 10000m, "100000032", 10000m, 350m),
        ]);

    [Fact]
    public void Generated_r2010_event_conforms_to_official_xsd_except_deferred_signature()
    {
        var xml = EfdReinfR2010XmlBuilder.Build(SampleInput());
        var doc = XDocument.Parse(xml);

        var events = doc.Descendants(XName.Get("Reinf", EvtNs)).ToList();
        Assert.Equal(2, events.Count); // one event document per (estab × prestador)

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

    [Fact]
    public void Combined_lote_conforms_to_both_r4020_and_r2010_xsds()
    {
        var r4020 = new ReinfR4020Input("2026-07", 2, new ReinfTaxpayer("99999999", "Empresa A"),
            [new ReinfR4020Payment("99999999000191", "11222333000181", "15009",
                new DateOnly(2026, 7, 15), 1000m, 1000m, 15m, 1000m, 10m, 1000m, 30m, 1000m, 6.5m)]);

        var xml = EfdReinfExport.BuildCombinedLote(r4020, SampleInput());
        var doc = XDocument.Parse(xml);
        var schemas = LoadSchemas();

        // R-4020 events (evtRetPJ) and R-2010 events (evtServTom) both live under the one lote, each event a
        // <Reinf> in its own event namespace — validate every event against the merged schema set.
        var r4020Events = doc.Descendants(XName.Get("Reinf", R4020Ns)).ToList();
        var r2010Events = doc.Descendants(XName.Get("Reinf", EvtNs)).ToList();
        Assert.Single(r4020Events);
        Assert.Equal(2, r2010Events.Count);

        foreach (var evt in r4020Events.Concat(r2010Events))
        {
            var blocking = Validate(evt, schemas)
                .Where(e => !e.Contains("Signature", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.True(blocking.Count == 0, "Erros de XSD (fora da assinatura):\n" + string.Join("\n", blocking));
        }
    }

    private const string R4020Ns = "http://www.reinf.esocial.gov.br/schemas/evt4020PagtoBeneficiarioPJ/v2_01_02";

    private static XmlSchemaSet LoadSchemas()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Fiscal", "schemas");
        // XmlResolver is null by default (blocks external refs) — enable it so each event XSD's relative
        // <xs:import> of xmldsig-core-schema.xsd resolves; otherwise ds:Signature stays undeclared.
        var set = new XmlSchemaSet { XmlResolver = new XmlUrlResolver() };
        set.Add(EvtNs, Path.Combine(dir, "R-2010-evtTomadorServicos-v2_01_02_B.xsd"));
        set.Add(R4020Ns, Path.Combine(dir, "R-4020-evt4020PagtoBeneficiarioPJ-v2_01_02_B.xsd"));
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
