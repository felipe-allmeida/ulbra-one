using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Accounting.Domain;
using Xunit;

namespace Ulbra.One.UnitTests.Accounting;

public class IncomeStatementCsvTests
{
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 12, 31);

    private static PostedLine Line(string code, AccountGroup group, DreLine dre, EntrySide side, decimal amount) =>
        new(Guid.NewGuid(), code, code, group, NormalBalance.Debit, side, amount, From, DreLine: dre);

    private static IncomeStatement Sample() => IncomeStatementCalculator.Build(
        [
            Line("3.2.05.10.020", AccountGroup.Revenue, DreLine.GrossRevenue, EntrySide.Credit, 1000m),
            Line("4.2.05.05.020", AccountGroup.Expense, DreLine.OperatingExpenses, EntrySide.Debit, 300m),
        ],
        From, To);

    [Fact]
    public void Has_header_and_crlf_rows()
    {
        var csv = IncomeStatementCsv.Build(Sample());
        Assert.StartsWith("tipo;codigo;descricao;valor\r\n", csv);
        Assert.EndsWith("\r\n", csv);
    }

    [Fact]
    public void Emits_account_rows_and_running_subtotals()
    {
        var csv = IncomeStatementCsv.Build(Sample());

        // Account detail row (comma decimals, code + name).
        Assert.Contains("Conta;3.2.05.10.020;3.2.05.10.020;1000,00", csv);
        // Expense contributes negatively (credit − debit).
        Assert.Contains("Conta;4.2.05.05.020;4.2.05.05.020;-300,00", csv);
        // Running subtotals of the cascade.
        Assert.Contains("Subtotal;;Receita Operacional Líquida;1000,00", csv);
        Assert.Contains("Subtotal;;Lucro/Prejuízo Líquido do Exercício;700,00", csv);
    }

    [Fact]
    public void Omits_financial_subtotal_when_there_is_no_financial_activity()
    {
        var csv = IncomeStatementCsv.Build(Sample());
        Assert.DoesNotContain("Resultado Financeiro", csv);
    }

    [Fact]
    public void Empty_statement_still_emits_the_zeroed_subtotals()
    {
        var csv = IncomeStatementCsv.Build(IncomeStatementCalculator.Build([], From, To));
        Assert.Contains("Subtotal;;Lucro/Prejuízo Líquido do Exercício;0,00", csv);
    }
}
