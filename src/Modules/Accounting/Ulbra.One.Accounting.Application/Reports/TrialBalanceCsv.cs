using System.Globalization;
using System.Text;

namespace Ulbra.One.Accounting.Application.Reports;

/// <summary>Pure builder of the Balancete CSV (Excel pt-BR): <c>;</c>-separated, comma decimals, CRLF, one row per
/// account (código · código reduzido · conta · saldo anterior · débitos · créditos · saldo final) followed by a
/// totals row (period debit/credit — the double-entry proof), mirroring the on-screen footer. Same CSV
/// conventions as <see cref="GeneralLedgerCsv"/>, including the formula-injection guard. No EF, no I/O.</summary>
public static class TrialBalanceCsv
{
    private const string Header = "codigo;codigo_reduzido;conta;saldo_anterior;debitos;creditos;saldo_final";

    public static string Build(TrialBalance trialBalance)
    {
        ArgumentNullException.ThrowIfNull(trialBalance);

        var sb = new StringBuilder();
        sb.Append(Header).Append("\r\n");

        foreach (var r in trialBalance.Rows)
            sb.Append(string.Join(';',
                Field(r.AccountCode), Field(r.ReducedCode ?? ""), Field(r.AccountName),
                Money(r.OpeningBalance), Money(r.TotalDebit), Money(r.TotalCredit), Money(r.Balance)))
              .Append("\r\n");

        if (trialBalance.Rows.Count > 0)
            sb.Append(string.Join(';',
                "", "", Field("Totais"), "", Money(trialBalance.TotalDebit), Money(trialBalance.TotalCredit), ""))
              .Append("\r\n");

        return sb.ToString();
    }

    private static string Money(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');

    private static string Field(string v)
    {
        // CSV formula injection: a leading = + - @ (or a control char) makes Excel/Sheets evaluate the cell as a
        // formula. Prefix a single quote so it stays text (OWASP). Numeric columns use Money(), not Field.
        if (v.Length > 0 && "=+-@\t\r".IndexOf(v[0]) >= 0) v = "'" + v;
        return v.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }
}
