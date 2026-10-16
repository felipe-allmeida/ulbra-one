using System.Globalization;
using System.Text;
using System.Xml;
using Ulbra.One.Common.Domain.Validation;
using Ulbra.One.Fiscal.Domain.Assessment;

namespace Ulbra.One.Fiscal.Domain.EfdReinf;

/// <summary>One nota fiscal de serviço tomado = one <c>nfs</c> of R-2010 (one per título/payable). Carries the
/// gross of the nota (<c>vlrBruto</c>), the NF série/número/emissão, the INSS base and withheld amount, and the
/// tipo de serviço (Tabela 06). Establishment (tomador) × prestador group the parent <c>evtServTom</c>.</summary>
public sealed record ReinfR2010Nf(
    string EstabCnpj,
    string PrestadorCnpj,
    string Serie,
    string NumDocto,
    DateOnly DtEmissao,
    decimal VlrBruto,
    string TpServico,
    decimal VlrBaseRet,
    decimal VlrRetencao);

/// <summary>Input to the R-2010 writer: contribuinte (tomador) + competência + ambiente (tpAmb) + the projected
/// notas fiscais de serviço tomado.</summary>
public sealed record ReinfR2010Input(
    string PerApur, int TpAmb, ReinfTaxpayer Taxpayer, IReadOnlyList<ReinfR2010Nf> Notas);

/// <summary>Projects the withholding facts into R-2010 notas — only the INSS guia (previdenciária sobre cessão de
/// mão de obra/empreitada). One <see cref="ReinfR2010Nf"/> per título (payable); base and amount summed across the
/// INSS lines of the título. IRRF/CSRF (R-4020) / ISS (municipal) / Other are excluded. Série defaults to "1"
/// (NFS-e usually has none — 🔶 homologate); número comes from the payable document (truncated to the XSD's 15
/// chars), "SN" when absent; tipo de serviço via <see cref="TipoServicoResolver"/>.</summary>
public static class ReinfR2010Projection
{
    public static IReadOnlyList<ReinfR2010Nf> FromFacts(IEnumerable<WithholdingFact> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return facts
            .Where(f => ReinfEventMapper.Map(f.Group) == ReinfEvent.R2010)
            .GroupBy(f => f.PayableId)
            .Select(g =>
            {
                var first = g.First(); // CNPJs / subitem / gross / doc belong to the título — same on every line
                // Strip any CNPJ mask (Company.Cnpj / Vendor.Document are not digit-normalized on write) so
                // nrInscEstab/cnpjPrestador stay digits-only per the XSD ([0-9]{12}|[0-9]{14} and \d{14}).
                return new ReinfR2010Nf(
                    DocumentValidator.Normalize(first.CompanyCnpj),
                    DocumentValidator.Normalize(first.VendorDocument),
                    "1", // 🔶 série — NFS-e frequently carries none; default "1" (homologate with Accounting)
                    NumDocto(first.DocumentNumber),
                    first.EventDate,
                    first.GrossAmount,
                    TipoServicoResolver.Resolve(first.Lc116Subitem),
                    g.Sum(f => f.BaseAmount),
                    g.Sum(f => f.Amount));
            })
            .OrderBy(n => n.EstabCnpj, StringComparer.Ordinal)
            .ThenBy(n => n.PrestadorCnpj, StringComparer.Ordinal)
            .ThenBy(n => n.DtEmissao)
            .ThenBy(n => n.NumDocto, StringComparer.Ordinal)
            .ToList();
    }

    // numDocto in the XSD is a free string, minLength 1 / maxLength 15. Fall back to "SN" (sem número) when the
    // payable has no document number; truncate long numbers to the 15-char ceiling.
    private static string NumDocto(string? documentNumber)
    {
        var n = documentNumber?.Trim();
        if (string.IsNullOrEmpty(n)) return "SN";
        return n.Length > 15 ? n[..15] : n;
    }
}

/// <summary>Pure builder of the R-2010 XML (leiaute 2.1.2, schema <c>evtTomadorServicos/v2_01_02</c>) — no I/O.
/// The lote (<c>envioLoteEventos</c>) wraps <b>one event per (estabelecimento tomador × prestador)</b>: in the XSD
/// <c>ideEstabObra</c> and <c>idePrestServ</c> are 1..1; inside, one <c>nfs</c> per título and one <c>infoTpServ</c>
/// per nota (single tipo de serviço, MVP). The <c>ds:Signature</c> (required by the event XSD) is added at signing
/// — a deferred step.</summary>
public static class EfdReinfR2010XmlBuilder
{
    private const string EvtNs = "http://www.reinf.esocial.gov.br/schemas/evtTomadorServicos/v2_01_02";

    public static string Build(ReinfR2010Input input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var sb = new StringBuilder();
        using (var w = XmlWriter.Create(sb, new XmlWriterSettings { Indent = true, OmitXmlDeclaration = true }))
        {
            w.WriteStartElement("Reinf", EfdReinfXmlBuilder.EnvNs);
            w.WriteStartElement("loteEventos", EfdReinfXmlBuilder.EnvNs);
            var seq = 1;
            WriteEventos(w, input, ref seq);
            w.WriteEndElement(); // loteEventos
            w.WriteEndElement(); // Reinf (lote)
        }
        return sb.ToString();
    }

    /// <summary>Writes the R-2010 <c>evento</c> blocks (one per estabelecimento tomador × prestador) into an
    /// already-open lote. Shared by the standalone <see cref="Build"/> and the combined R-4020+R-2010 export.</summary>
    internal static void WriteEventos(XmlWriter w, ReinfR2010Input input, ref int seq)
    {
        foreach (var grp in input.Notas
            .GroupBy(n => (n.EstabCnpj, n.PrestadorCnpj))
            .OrderBy(g => g.Key.EstabCnpj, StringComparer.Ordinal)
            .ThenBy(g => g.Key.PrestadorCnpj, StringComparer.Ordinal))
        {
            var id = EfdReinfXmlBuilder.EventId(grp.Key.EstabCnpj, seq++);
            var notas = grp.OrderBy(n => n.DtEmissao).ThenBy(n => n.NumDocto, StringComparer.Ordinal).ToList();

            var totalBruto = notas.Sum(n => n.VlrBruto);
            var totalBaseRet = notas.Sum(n => n.VlrBaseRet);
            var totalRetPrinc = notas.Sum(n => n.VlrRetencao);
            // indCPRB derived from the effective rate: 3,5% (< 5%) ⇒ prestador na CPRB (folha desonerada, Lei
            // 12.546); senão 11%. Holds while only 11%/3,5% exist (aposentadoria especial additional is out of
            // scope). 🔶 Revisit if a new INSS rate is introduced.
            var indCprb = totalBaseRet > 0 && totalRetPrinc / totalBaseRet < 0.05m ? "1" : "0";

            w.WriteStartElement("evento", EfdReinfXmlBuilder.EnvNs);
            w.WriteAttributeString("id", id);
            w.WriteStartElement("Reinf", EvtNs); // root of the event document (event namespace)
            w.WriteStartElement("evtServTom", EvtNs);
            w.WriteAttributeString("id", id);

            w.WriteStartElement("ideEvento", EvtNs);
            w.WriteElementString("indRetif", EvtNs, "1");
            w.WriteElementString("perApur", EvtNs, input.PerApur);
            w.WriteElementString("tpAmb", EvtNs, input.TpAmb.ToString(CultureInfo.InvariantCulture));
            w.WriteElementString("procEmi", EvtNs, "1");
            w.WriteElementString("verProc", EvtNs, "UlbraOne");
            w.WriteEndElement(); // ideEvento

            w.WriteStartElement("ideContri", EvtNs);
            w.WriteElementString("tpInsc", EvtNs, "1");
            w.WriteElementString("nrInsc", EvtNs, input.Taxpayer.Root);
            w.WriteEndElement(); // ideContri

            w.WriteStartElement("infoServTom", EvtNs);
            w.WriteStartElement("ideEstabObra", EvtNs);
            w.WriteElementString("tpInscEstab", EvtNs, "1");
            w.WriteElementString("nrInscEstab", EvtNs, grp.Key.EstabCnpj);
            w.WriteElementString("indObra", EvtNs, "0"); // não é obra de construção civil (obra/CEI fora de escopo)

            w.WriteStartElement("idePrestServ", EvtNs);
            w.WriteElementString("cnpjPrestador", EvtNs, grp.Key.PrestadorCnpj);
            w.WriteElementString("vlrTotalBruto", EvtNs, EfdReinfXmlBuilder.Money(totalBruto));
            w.WriteElementString("vlrTotalBaseRet", EvtNs, EfdReinfXmlBuilder.Money(totalBaseRet));
            w.WriteElementString("vlrTotalRetPrinc", EvtNs, EfdReinfXmlBuilder.Money(totalRetPrinc));
            w.WriteElementString("indCPRB", EvtNs, indCprb);

            foreach (var nf in notas)
            {
                w.WriteStartElement("nfs", EvtNs);
                w.WriteElementString("serie", EvtNs, nf.Serie);
                w.WriteElementString("numDocto", EvtNs, nf.NumDocto);
                w.WriteElementString("dtEmissaoNF", EvtNs, nf.DtEmissao.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                w.WriteElementString("vlrBruto", EvtNs, EfdReinfXmlBuilder.Money(nf.VlrBruto));

                w.WriteStartElement("infoTpServ", EvtNs);
                w.WriteElementString("tpServico", EvtNs, nf.TpServico);
                w.WriteElementString("vlrBaseRet", EvtNs, EfdReinfXmlBuilder.Money(nf.VlrBaseRet));
                w.WriteElementString("vlrRetencao", EvtNs, EfdReinfXmlBuilder.Money(nf.VlrRetencao));
                w.WriteEndElement(); // infoTpServ

                w.WriteEndElement(); // nfs
            }

            w.WriteEndElement(); // idePrestServ
            w.WriteEndElement(); // ideEstabObra
            w.WriteEndElement(); // infoServTom
            w.WriteEndElement(); // evtServTom
            w.WriteEndElement(); // Reinf (evento)
            w.WriteEndElement(); // evento
        }
    }
}
