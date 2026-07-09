using System.Globalization;
using System.Text;
using System.Xml;
using Ulbra.One.Common.Domain.Validation;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Fiscal.Domain.Assessment;

namespace Ulbra.One.Fiscal.Domain.EfdReinf;

/// <summary>Contribuinte (8-digit root) of the lote — the ideContri of R-4020.</summary>
public sealed record ReinfTaxpayer(string Root, string Name);

/// <summary>A payment to a PJ beneficiary = one <c>infoPgto</c> of R-4020 (one per título/fato gerador). Carries the
/// gross of the título (<c>vlrBruto</c>), the fato gerador date (<c>dtFG</c>) and, per tribute, the base and the
/// withheld amount (IR/CSLL/COFINS/PIS-PASEP). There is no "CSRF" in the leiaute — each tribute has its own base.</summary>
public sealed record ReinfR4020Payment(
    string EstabCnpj,
    string BeneficiaryCnpj,
    string NatRend,
    DateOnly Dtfg,
    decimal Gross,
    decimal BaseIrrf, decimal Irrf,
    decimal BaseCsll, decimal Csll,
    decimal BaseCofins, decimal Cofins,
    decimal BasePis, decimal Pis);

/// <summary>Input to the R-4020 writer: contribuinte + competência + ambiente (tpAmb) + already-projected payments.</summary>
public sealed record ReinfR4020Input(
    string PerApur, int TpAmb, ReinfTaxpayer Taxpayer, IReadOnlyList<ReinfR4020Payment> Payments);

/// <summary>Projects the withholding facts into R-4020 payments — only IRRF/CSRF guias (série 4000 on PJ:
/// IR/CSLL/COFINS/PIS). One <see cref="ReinfR4020Payment"/> per título (payable) = one <c>infoPgto</c>; base and
/// amount summed per tribute within the título. INSS (R-2010) / ISS (municipal) / Other are excluded.</summary>
public static class ReinfR4020Projection
{
    public static IReadOnlyList<ReinfR4020Payment> FromFacts(IEnumerable<WithholdingFact> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return facts
            .Where(f => ReinfEventMapper.Map(f.Group) == ReinfEvent.R4020)
            .GroupBy(f => f.PayableId)
            .Select(g =>
            {
                var first = g.First(); // CNPJs / subitem / gross belong to the título — same on every line
                decimal Base(WithholdingTaxType t) => g.Where(f => f.TaxType == t).Sum(f => f.BaseAmount);
                decimal Ret(WithholdingTaxType t) => g.Where(f => f.TaxType == t).Sum(f => f.Amount);
                // Strip any CNPJ mask (Company.Cnpj is not digit-normalized on write) so nrInscEstab/cnpjBenef
                // stay digits-only per the XSD [0-9]{11}|[0-9]{14}.
                return new ReinfR4020Payment(
                    DocumentValidator.Normalize(first.CompanyCnpj), DocumentValidator.Normalize(first.VendorDocument),
                    IncomeNatureResolver.Resolve(first.Lc116Subitem, first.Group),
                    first.IssueDate, first.GrossAmount,
                    Base(WithholdingTaxType.Irrf), Ret(WithholdingTaxType.Irrf),
                    Base(WithholdingTaxType.Csll), Ret(WithholdingTaxType.Csll),
                    Base(WithholdingTaxType.Cofins), Ret(WithholdingTaxType.Cofins),
                    Base(WithholdingTaxType.Pis), Ret(WithholdingTaxType.Pis));
            })
            .OrderBy(p => p.EstabCnpj, StringComparer.Ordinal)
            .ThenBy(p => p.BeneficiaryCnpj, StringComparer.Ordinal)
            .ThenBy(p => p.NatRend, StringComparer.Ordinal)
            .ThenBy(p => p.Dtfg)
            .ToList();
    }
}

/// <summary>Pure builder of the R-4020 XML (leiaute 2.1.2, schema <c>evt4020PagtoBeneficiarioPJ/v2_01_02</c>) — no
/// I/O. The lote (<c>envioLoteEventos</c>) wraps <b>one event per (establishment × beneficiary)</b> — in the XSD
/// <c>ideEstab</c> and <c>ideBenef</c> are 1..1; inside, one <c>idePgto</c> per natureza and one <c>infoPgto</c>
/// per título. The <c>ds:Signature</c> (required by the event XSD) is added at signing — a deferred step.</summary>
public static class EfdReinfXmlBuilder
{
    // Lote-envelope schema (shared with R-2010) × the R-4020 event schema.
    internal const string EnvNs = "http://www.reinf.esocial.gov.br/schemas/envioLoteEventos/v1_05_01";
    private const string EvtNs = "http://www.reinf.esocial.gov.br/schemas/evt4020PagtoBeneficiarioPJ/v2_01_02";

    public static string Build(ReinfR4020Input input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var sb = new StringBuilder();
        using (var w = XmlWriter.Create(sb, new XmlWriterSettings { Indent = true, OmitXmlDeclaration = true }))
        {
            w.WriteStartElement("Reinf", EnvNs);
            w.WriteStartElement("loteEventos", EnvNs);
            var seq = 1;
            WriteEventos(w, input, ref seq);
            w.WriteEndElement(); // loteEventos
            w.WriteEndElement(); // Reinf (lote)
        }
        return sb.ToString();
    }

    /// <summary>Writes the R-4020 <c>evento</c> blocks (one per estabelecimento × beneficiário) into an already-open
    /// lote. Shared by the standalone <see cref="Build"/> and the combined R-4020+R-2010 export.</summary>
    internal static void WriteEventos(XmlWriter w, ReinfR4020Input input, ref int seq)
    {
        foreach (var evt in input.Payments
            .GroupBy(p => (p.EstabCnpj, p.BeneficiaryCnpj))
            .OrderBy(g => g.Key.EstabCnpj, StringComparer.Ordinal)
            .ThenBy(g => g.Key.BeneficiaryCnpj, StringComparer.Ordinal))
        {
            var id = EventId(evt.Key.EstabCnpj, seq++);

            w.WriteStartElement("evento", EnvNs);
            w.WriteAttributeString("id", id);
            w.WriteStartElement("Reinf", EvtNs); // root of the event document (event namespace)
            w.WriteStartElement("evtRetPJ", EvtNs);
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

            w.WriteStartElement("ideEstab", EvtNs);
            w.WriteElementString("tpInscEstab", EvtNs, "1");
            w.WriteElementString("nrInscEstab", EvtNs, evt.Key.EstabCnpj);

            w.WriteStartElement("ideBenef", EvtNs);
            w.WriteElementString("cnpjBenef", EvtNs, evt.Key.BeneficiaryCnpj);

            foreach (var nat in evt.GroupBy(p => p.NatRend).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                w.WriteStartElement("idePgto", EvtNs);
                w.WriteElementString("natRend", EvtNs, nat.Key);

                foreach (var pay in nat.OrderBy(p => p.Dtfg).ThenBy(p => p.Gross))
                {
                    w.WriteStartElement("infoPgto", EvtNs);
                    w.WriteElementString("dtFG", EvtNs, pay.Dtfg.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    w.WriteElementString("vlrBruto", EvtNs, Money(pay.Gross));
                    WriteRetencoes(w, pay);
                    w.WriteEndElement(); // infoPgto
                }
                w.WriteEndElement(); // idePgto
            }

            w.WriteEndElement(); // ideBenef
            w.WriteEndElement(); // ideEstab
            w.WriteEndElement(); // evtRetPJ
            w.WriteEndElement(); // Reinf (evento)
            w.WriteEndElement(); // evento
        }
    }

    /// <summary>Emits <c>retencoes</c> in XSD order, only the (base + withheld) pairs that apply. <c>retencoes</c>
    /// is 0..1 — omitted when the título has no série-4000 withholding.</summary>
    private static void WriteRetencoes(XmlWriter w, ReinfR4020Payment p)
    {
        var hasIr = p.Irrf > 0 || p.BaseIrrf > 0;
        var hasCsll = p.Csll > 0 || p.BaseCsll > 0;
        var hasCofins = p.Cofins > 0 || p.BaseCofins > 0;
        var hasPis = p.Pis > 0 || p.BasePis > 0;
        if (!hasIr && !hasCsll && !hasCofins && !hasPis) return;

        w.WriteStartElement("retencoes", EvtNs);
        if (hasIr) { w.WriteElementString("vlrBaseIR", EvtNs, Money(p.BaseIrrf)); w.WriteElementString("vlrIR", EvtNs, Money(p.Irrf)); }
        if (hasCsll) { w.WriteElementString("vlrBaseCSLL", EvtNs, Money(p.BaseCsll)); w.WriteElementString("vlrCSLL", EvtNs, Money(p.Csll)); }
        if (hasCofins) { w.WriteElementString("vlrBaseCofins", EvtNs, Money(p.BaseCofins)); w.WriteElementString("vlrCofins", EvtNs, Money(p.Cofins)); }
        if (hasPis) { w.WriteElementString("vlrBasePP", EvtNs, Money(p.BasePis)); w.WriteElementString("vlrPP", EvtNs, Money(p.Pis)); }
        w.WriteEndElement(); // retencoes
    }

    /// <summary>Event id in the XSD pattern (<c>I{1}D{1}[0-9]{34}</c>): ID + tpInscr(1) + estab CNPJ(14) +
    /// timestamp (14-zero placeholder — to compose at transmissão/assinatura, deferred) + sequential(5).</summary>
    internal static string EventId(string estabCnpj, int seq)
    {
        var digits = new string(estabCnpj.Where(char.IsDigit).ToArray());
        var cnpj14 = digits.Length >= 14 ? digits[..14] : digits.PadLeft(14, '0');
        return $"ID1{cnpj14}00000000000000{seq:D5}";
    }

    // Monetary values in the leiaute format: comma decimal, 2 places ([0-9]{1,12}[,][0-9]{2}).
    internal static string Money(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');
}
