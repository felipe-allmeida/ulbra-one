using System.Globalization;
using System.Text;

namespace Ulbra.One.Fiscal.Domain.Assessment;

/// <summary>Pure builder of the format-agnostic withholding CSV (valores retidos) — one row per withholding line,
/// `;`-separated (pt-BR/Excel), comma decimals. No EF, no I/O, no event codes (that's the EFD-Reinf adapter).</summary>
public static class AssessmentCsv
{
    private const string Header = "competencia;empresa;cnpj;centro_custo;fornecedor;documento;guia;tributo;base;retido";

    public static string Build(string perApur, IEnumerable<WithholdingFact> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var sb = new StringBuilder();
        sb.Append(Header).Append("\r\n");
        foreach (var f in facts)
        {
            sb.Append(string.Join(';',
                Field(perApur), Field(f.CompanyName), Field(f.CompanyCnpj), Field(f.CostCenterName),
                Field(f.CounterpartyName), Field(f.DocumentNumber ?? string.Empty),
                Field(f.Group.ToString()), Field(f.TaxType.ToString()), Money(f.BaseAmount), Money(f.Amount)));
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    private static string Money(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');

    private static string Field(string v) =>
        v.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
}
