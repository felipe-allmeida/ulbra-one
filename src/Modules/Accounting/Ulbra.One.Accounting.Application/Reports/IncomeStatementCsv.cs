using System.Globalization;
using System.Text;
using Ulbra.One.Accounting.Domain;

namespace Ulbra.One.Accounting.Application.Reports;

/// <summary>Pure builder of the DRE CSV (Excel pt-BR): <c>;</c>-separated, comma decimals, CRLF. Emits the
/// cascade in reading order — one row per DRE line (label + total), the account rows under it, and the running
/// subtotals (net revenue → gross profit → operating result → financial result → pre-tax result (LAIR) → net
/// income). The <c>tipo</c> column (Linha/Conta/Subtotal — the CSV's pt-BR row-type labels) lets the reader
/// filter. Amounts are contributions to the result (revenue +, cost/expense −), as on screen. Same CSV
/// conventions as <see cref="TrialBalanceCsv"/> (BOM added by the endpoint; formula-injection guard). No EF, no I/O.</summary>
public static class IncomeStatementCsv
{
    private const string Header = "tipo;codigo;descricao;valor";

    public static string Build(IncomeStatement dre)
    {
        ArgumentNullException.ThrowIfNull(dre);

        var sb = new StringBuilder();
        sb.Append(Header).Append("\r\n");

        void Section(DreLine line)
        {
            var section = dre.Sections.FirstOrDefault(s => s.Line == line);
            if (section is null) return;
            Row("Linha", "", LineLabel(line), section.Total);
            foreach (var a in section.Lines) Row("Conta", a.AccountCode, a.AccountName, a.Amount);
        }
        void Subtotal(string label, decimal value) => Row("Subtotal", "", label, value);
        void Row(string tipo, string code, string desc, decimal value) =>
            sb.Append(string.Join(';', Field(tipo), Field(code), Field(desc), Money(value))).Append("\r\n");

        Section(DreLine.GrossRevenue);
        Section(DreLine.RevenueDeductions);
        Subtotal("Receita Operacional Líquida", dre.NetRevenue);
        Section(DreLine.CostOfSales);
        Subtotal("Lucro Bruto", dre.GrossProfit);
        Section(DreLine.OtherOperatingIncome);
        Section(DreLine.OperatingExpenses);
        Subtotal("Resultado Operacional", dre.OperatingResult);
        Section(DreLine.FinancialRevenue);
        Section(DreLine.FinancialExpense);
        if (HasFinancial(dre)) Subtotal("Resultado Financeiro", dre.FinancialResult);
        Section(DreLine.Other);
        Subtotal("Resultado Antes do IRPJ/CSLL (LAIR)", dre.PreTaxResult);
        Section(DreLine.IncomeTax);
        Section(DreLine.SocialContribution);
        Subtotal("Lucro/Prejuízo Líquido do Exercício", dre.NetIncome);

        return sb.ToString();
    }

    private static bool HasFinancial(IncomeStatement dre) =>
        dre.Sections.Any(s => s.Line is DreLine.FinancialRevenue or DreLine.FinancialExpense);

    private static string LineLabel(DreLine line) => line switch
    {
        DreLine.GrossRevenue => "Receita Operacional Bruta",
        DreLine.RevenueDeductions => "(-) Deduções da Receita Bruta",
        DreLine.CostOfSales => "(-) Custo dos Bens e Serviços (CMV/CSV)",
        DreLine.OtherOperatingIncome => "Outras Receitas Operacionais",
        DreLine.OperatingExpenses => "(-) Despesas Operacionais",
        DreLine.FinancialRevenue => "Receitas Financeiras",
        DreLine.FinancialExpense => "(-) Despesas Financeiras",
        DreLine.Other => "Outros Resultados",
        DreLine.IncomeTax => "(-) Provisão para IRPJ",
        DreLine.SocialContribution => "(-) Provisão para CSLL",
        _ => line.ToString(),
    };

    private static string Money(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');

    private static string Field(string v)
    {
        // CSV formula injection: a leading = + - @ (or a control char) makes Excel/Sheets evaluate the cell as a
        // formula. Prefix a single quote so it stays text (OWASP). Numeric column uses Money(), not Field.
        if (v.Length > 0 && "=+-@\t\r".IndexOf(v[0]) >= 0) v = "'" + v;
        return v.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }
}
