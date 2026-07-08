using System.Text;
using System.Xml;

namespace Ulbra.One.Fiscal.Domain.EfdReinf;

/// <summary>Builds the "Obrigações Acessórias" export: a single lote (<c>envioLoteEventos</c>) carrying both event
/// types of the competência — R-4020 (evtRetPJ — IR/CSLL/PIS/COFINS on PJ) and R-2010 (evtServTom — INSS on serviços
/// tomados). The two builders share one sequential counter so every <c>evento id</c> in the lote stays unique.</summary>
public static class EfdReinfExport
{
    public static string BuildCombinedLote(ReinfR4020Input r4020, ReinfR2010Input r2010)
    {
        ArgumentNullException.ThrowIfNull(r4020);
        ArgumentNullException.ThrowIfNull(r2010);

        var sb = new StringBuilder();
        using (var w = XmlWriter.Create(sb, new XmlWriterSettings { Indent = true, OmitXmlDeclaration = true }))
        {
            w.WriteStartElement("Reinf", EfdReinfXmlBuilder.EnvNs);
            w.WriteStartElement("loteEventos", EfdReinfXmlBuilder.EnvNs);
            var seq = 1;
            EfdReinfXmlBuilder.WriteEventos(w, r4020, ref seq);
            EfdReinfR2010XmlBuilder.WriteEventos(w, r2010, ref seq);
            w.WriteEndElement(); // loteEventos
            w.WriteEndElement(); // Reinf (lote)
        }
        return sb.ToString();
    }
}
