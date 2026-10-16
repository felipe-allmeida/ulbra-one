using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Accounting.Domain;
using Xunit;

namespace Ulbra.One.UnitTests.Accounting;

public class TrialBalanceCsvTests
{
    private const string Header = "codigo;codigo_reduzido;conta;saldo_anterior;debitos;creditos;saldo_final";

    [Fact]
    public void Builds_excel_friendly_csv_with_totals()
    {
        var rows = new List<TrialBalanceRow>
        {
            new(Guid.NewGuid(), "1.1.01", "R123", "Caixa; Matriz", AccountGroup.Asset, NormalBalance.Debit, 100m, 1234.56m, 0m, 1334.56m),
            new(Guid.NewGuid(), "4.1", null, "=CMD()", AccountGroup.Expense, NormalBalance.Debit, 0m, 34.5m, 0m, 34.5m),
        };
        var csv = TrialBalanceCsv.Build(new TrialBalance(rows, 1269.06m, 0m));

        Assert.StartsWith(Header + "\r\n", csv);
        Assert.Contains("1.1.01;R123;\"Caixa; Matriz\";100,00;1234,56;0,00;1334,56", csv); // quoted, comma decimals
        Assert.Contains("4.1;;'=CMD()", csv);            // empty reduced code + formula-injection guard
        Assert.Contains(";;Totais;;1269,06;0,00;", csv); // totals row mirrors the on-screen footer
    }

    [Fact]
    public void Empty_yields_header_only() =>
        Assert.Equal(Header + "\r\n", TrialBalanceCsv.Build(new TrialBalance([], 0m, 0m)));
}
