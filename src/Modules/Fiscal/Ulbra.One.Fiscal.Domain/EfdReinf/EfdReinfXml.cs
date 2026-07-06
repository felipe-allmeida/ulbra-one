using System.Globalization;
using System.Text;
using System.Xml;
using Ulbra.One.Common.Domain.Validation;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Fiscal.Domain.Assessment;

namespace Ulbra.One.Fiscal.Domain.EfdReinf;

/// <summary>Contribuinte (raiz de 8 dígitos) do lote — o ideContri do R-4020.</summary>
public sealed record ReinfContribuinte(string Raiz, string Name);

/// <summary>Um pagamento a beneficiário PJ = um <c>infoPgto</c> do R-4020 (um por título/fato gerador). Carrega o
/// bruto do título (<c>vlrBruto</c>), a data do fato gerador (<c>dtFG</c>) e, por tributo, a base e o valor retido
/// (IR/CSLL/COFINS/PIS-PASEP). Não existe "CSRF" no leiaute — cada tributo tem base própria.</summary>
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

/// <summary>Entrada do writer do R-4020: contribuinte + competência + ambiente + pagamentos já projetados.</summary>
public sealed record ReinfR4020Input(
    string PerApur, int TpAmb, ReinfContribuinte Contribuinte, IReadOnlyList<ReinfR4020Payment> Payments);

/// <summary>Projeta os fatos de retenção nos pagamentos do R-4020 — só guias IRRF/CSRF (série 4000 sobre PJ:
/// IR/CSLL/COFINS/PIS). Um <see cref="ReinfR4020Payment"/> por título (payable) = um <c>infoPgto</c>; base e valor
/// somados por tributo dentro do título. INSS (R-2010) / ISS (municipal) / Other ficam de fora.</summary>
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
                var first = g.First(); // CNPJs / subitem / bruto são do título — iguais em todas as linhas
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

/// <summary>Builder puro do XML do R-4020 (leiaute 2.1.2, schema <c>evt4020PagtoBeneficiarioPJ/v2_01_02</c>) — sem
/// I/O. O lote (<c>envioLoteEventos</c>) envolve <b>um evento por (estabelecimento × beneficiário)</b> — no XSD
/// <c>ideEstab</c> e <c>ideBenef</c> são 1..1; dentro, um <c>idePgto</c> por natureza e um <c>infoPgto</c> por
/// título. A <c>ds:Signature</c> (exigida pelo XSD do evento) é adicionada na assinatura — etapa deferida.</summary>
public static class EfdReinfXmlBuilder
{
    // Envelope de envio de lote (não coberto pelo XSD do evento) × schema do evento R-4020.
    private const string EnvNs = "http://www.reinf.esocial.gov.br/schemas/envioLoteEventos/v1_05_01";
    private const string EvtNs = "http://www.reinf.esocial.gov.br/schemas/evt4020PagtoBeneficiarioPJ/v2_01_02";

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
            foreach (var evt in input.Payments
                .GroupBy(p => (p.EstabCnpj, p.BeneficiaryCnpj))
                .OrderBy(g => g.Key.EstabCnpj, StringComparer.Ordinal)
                .ThenBy(g => g.Key.BeneficiaryCnpj, StringComparer.Ordinal))
            {
                var id = EventId(evt.Key.EstabCnpj, seq++);

                w.WriteStartElement("evento", EnvNs);
                w.WriteAttributeString("id", id);
                w.WriteStartElement("Reinf", EvtNs); // raiz do documento de evento (namespace do evento)
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
                w.WriteElementString("nrInsc", EvtNs, input.Contribuinte.Raiz);
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

            w.WriteEndElement(); // loteEventos
            w.WriteEndElement(); // Reinf (lote)
        }
        return sb.ToString();
    }

    /// <summary>Emite <c>retencoes</c> na ordem do XSD, só os pares (base + retido) que se aplicam. <c>retencoes</c>
    /// é 0..1 — omitido se o título não tem nenhuma retenção da série 4000.</summary>
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

    /// <summary>Id do evento no padrão do XSD (<c>I{1}D{1}[0-9]{34}</c>): ID + tpInscr(1) + CNPJ estab(14) +
    /// carimbo de tempo (placeholder de 14 zeros — a compor na transmissão/assinatura, deferida) + sequencial(5).</summary>
    private static string EventId(string estabCnpj, int seq)
    {
        var digits = new string(estabCnpj.Where(char.IsDigit).ToArray());
        var cnpj14 = digits.Length >= 14 ? digits[..14] : digits.PadLeft(14, '0');
        return $"ID1{cnpj14}00000000000000{seq:D5}";
    }

    // Valores monetários no padrão do leiaute: vírgula decimal, 2 casas ([0-9]{1,12}[,][0-9]{2}).
    private static string Money(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');
}
