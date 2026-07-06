using System.Globalization;
using System.Text;
using System.Xml;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Fiscal.Domain.Assessment;

namespace Ulbra.One.Fiscal.Domain.EfdReinf;

/// <summary>Contribuinte (raiz de 8 dígitos) do lote — o ideContri do R-4020.</summary>
public sealed record ReinfContribuinte(string Raiz, string Name);

/// <summary>Uma linha do R-4020 consolidada por estabelecimento × beneficiário (PJ) × natureza do rendimento.
/// Base = valor do serviço somado por título; valores por tributo já somados na competência.</summary>
public sealed record ReinfR4020Item(
    string EstabCnpj,
    string BeneficiaryCnpj,
    string NatRend,
    decimal BaseAmount,
    decimal Irrf,
    decimal Pis,
    decimal Cofins,
    decimal Csll);

/// <summary>Entrada do writer do R-4020: contribuinte + competência + ambiente + itens já agrupados.</summary>
public sealed record ReinfR4020Input(
    string PerApur, int TpAmb, ReinfContribuinte Contribuinte, IReadOnlyList<ReinfR4020Item> Items);

/// <summary>Projeta os fatos de retenção nos itens do R-4020 — só guias IRRF/CSRF (série 4000 sobre PJ),
/// agrupando por estabelecimento × beneficiário × natureza. INSS/ISS/Other ficam de fora.</summary>
public static class ReinfR4020Projection
{
    public static IReadOnlyList<ReinfR4020Item> FromFacts(IEnumerable<WithholdingFact> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return facts
            .Where(f => ReinfEventMapper.Map(f.Group) == ReinfEvent.R4020)
            .GroupBy(f => (f.CompanyCnpj, f.VendorDocument,
                NatRend: IncomeNatureResolver.Resolve(f.Lc116Subitem, f.Group)))
            .Select(g => new ReinfR4020Item(
                g.Key.CompanyCnpj, g.Key.VendorDocument, g.Key.NatRend,
                // Base = valor do serviço por título (uma vez por payable, não por linha de tributo).
                g.GroupBy(f => f.PayableId).Sum(pg => pg.First().BaseAmount),
                g.Where(f => f.TaxType == WithholdingTaxType.Irrf).Sum(f => f.Amount),
                g.Where(f => f.TaxType == WithholdingTaxType.Pis).Sum(f => f.Amount),
                g.Where(f => f.TaxType == WithholdingTaxType.Cofins).Sum(f => f.Amount),
                g.Where(f => f.TaxType == WithholdingTaxType.Csll).Sum(f => f.Amount)))
            .OrderBy(i => i.EstabCnpj, StringComparer.Ordinal)
            .ThenBy(i => i.BeneficiaryCnpj, StringComparer.Ordinal)
            .ThenBy(i => i.NatRend, StringComparer.Ordinal)
            .ToList();
    }
}

/// <summary>Builder puro do XML do R-4020 (leiaute 2.1.2) — sem I/O. Um <c>evtRetPJ</c> por estabelecimento;
/// dentro, um <c>ideBenef</c> por beneficiário e um <c>idePgto</c> por natureza.
/// 🔶 Estrutura a validar contra o XSD oficial (AEL-627).</summary>
public static class EfdReinfXmlBuilder
{
    private const string EnvNs = "http://www.reinf.esocial.gov.br/schemas/envioLoteEventos/v1_05_01";
    private const string EvtNs = "http://www.reinf.esocial.gov.br/schemas/evtRetPJ/v2_01_02";

    public static string Build(ReinfR4020Input input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { Indent = true, OmitXmlDeclaration = true };
        using (var w = XmlWriter.Create(sb, settings))
        {
            w.WriteStartElement("Reinf", EnvNs);
            w.WriteStartElement("loteEventos", EnvNs);

            var seq = 1;
            foreach (var estab in input.Items.GroupBy(i => i.EstabCnpj).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                w.WriteStartElement("evento", EnvNs);
                w.WriteAttributeString("id", $"ID{seq++:D17}");
                w.WriteStartElement("evtRetPJ", EvtNs);

                w.WriteStartElement("ideEvento", EvtNs);
                w.WriteElementString("indRetif", EvtNs, "1");
                w.WriteElementString("perApur", EvtNs, input.PerApur);
                w.WriteElementString("tpAmb", EvtNs, input.TpAmb.ToString(CultureInfo.InvariantCulture));
                w.WriteElementString("procEmi", EvtNs, "1");
                w.WriteElementString("verProc", EvtNs, "UlbraOne");
                w.WriteEndElement();

                w.WriteStartElement("ideContri", EvtNs);
                w.WriteElementString("tpInscr", EvtNs, "1");
                w.WriteElementString("nrInscr", EvtNs, input.Contribuinte.Raiz);
                w.WriteEndElement();

                w.WriteStartElement("ideEstab", EvtNs);
                w.WriteElementString("tpInscrEstab", EvtNs, "1");
                w.WriteElementString("nrInscrEstab", EvtNs, estab.Key);

                foreach (var benef in estab.GroupBy(i => i.BeneficiaryCnpj).OrderBy(g => g.Key, StringComparer.Ordinal))
                {
                    w.WriteStartElement("ideBenef", EvtNs);
                    w.WriteElementString("cnpjBenef", EvtNs, benef.Key);

                    foreach (var item in benef.OrderBy(i => i.NatRend, StringComparer.Ordinal))
                    {
                        w.WriteStartElement("idePgto", EvtNs);
                        w.WriteElementString("natRend", EvtNs, item.NatRend);
                        w.WriteStartElement("infoPgto", EvtNs);
                        w.WriteElementString("vlrBaseIR", EvtNs, Money(item.BaseAmount));
                        w.WriteElementString("vlrIR", EvtNs, Money(item.Irrf));
                        w.WriteElementString("vlrBaseCSRF", EvtNs, Money(item.BaseAmount));
                        w.WriteElementString("vlrPisPasep", EvtNs, Money(item.Pis));
                        w.WriteElementString("vlrCofins", EvtNs, Money(item.Cofins));
                        w.WriteElementString("vlrCsll", EvtNs, Money(item.Csll));
                        w.WriteEndElement(); // infoPgto
                        w.WriteEndElement(); // idePgto
                    }
                    w.WriteEndElement(); // ideBenef
                }
                w.WriteEndElement(); // ideEstab
                w.WriteEndElement(); // evtRetPJ
                w.WriteEndElement(); // evento
            }

            w.WriteEndElement(); // loteEventos
            w.WriteEndElement(); // Reinf
        }
        return sb.ToString();
    }

    private static string Money(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);
}
