using System.Globalization;
using System.Text;

namespace Ulbra.One.Accounting.Application.Reports;

/// <summary>Pure builder of the Livro Razão CSV (Excel pt-BR): <c>;</c>-separated, comma decimals, CRLF, one row
/// per movement (account columns repeated) preceded by an opening-balance row per account, so the accumulated
/// balance column is self-contained. Mirrors the repo's CSV conventions (Fiscal <c>AssessmentCsv</c>), including
/// the CSV formula-injection guard. No EF, no I/O — cost-center labels are resolved by the caller.</summary>
public static class GeneralLedgerCsv
{
    private const string Header =
        "conta;codigo_resumido;nome_conta;data;historico;documento;centro_custo;debito;credito;saldo_acumulado";

    public static string Build(
        IReadOnlyList<GeneralLedgerAccount> accounts, IReadOnlyDictionary<Guid, string> costCenterLabels)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(costCenterLabels);

        var sb = new StringBuilder();
        sb.Append(Header).Append("\r\n");

        foreach (var a in accounts)
        {
            // Opening-balance row so the accumulated-balance column stands on its own.
            AppendRow(sb, a, date: "", history: "Saldo de abertura", document: "", costCenter: "",
                debit: null, credit: null, balance: a.OpeningBalance);

            foreach (var m in a.Movements)
            {
                var cc = m.CostCenterId is { } id && costCenterLabels.TryGetValue(id, out var label) ? label : "";
                AppendRow(sb, a,
                    date: m.CompetenceDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                    history: m.History, document: m.DocumentRef ?? "", costCenter: cc,
                    debit: m.Debit, credit: m.Credit, balance: m.AccumulatedBalance);
            }
        }

        return sb.ToString();
    }

    private static void AppendRow(
        StringBuilder sb, GeneralLedgerAccount a, string date, string history, string document, string costCenter,
        decimal? debit, decimal? credit, decimal balance)
    {
        sb.Append(string.Join(';',
            Field(a.AccountCode), Field(a.ReducedCode ?? ""), Field(a.AccountName),
            Field(date), Field(history), Field(document), Field(costCenter),
            MoneyOrEmpty(debit), MoneyOrEmpty(credit), Money(balance)));
        sb.Append("\r\n");
    }

    private static string MoneyOrEmpty(decimal? v) => v is { } d && d != 0 ? Money(d) : "";

    private static string Money(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');

    private static string Field(string v)
    {
        // CSV formula injection: a leading = + - @ (or a control char) makes Excel/Sheets evaluate the cell as a
        // formula. Prefix a single quote so it stays text (OWASP). Numeric columns use Money(), not Field.
        if (v.Length > 0 && "=+-@\t\r".IndexOf(v[0]) >= 0) v = "'" + v;
        return v.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }
}
