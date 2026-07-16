using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Accounting.Domain;
using Xunit;

namespace Ulbra.One.UnitTests.Accounting;

public class GeneralLedgerCsvTests
{
    private const string Header =
        "conta;codigo_resumido;nome_conta;data;historico;documento;centro_custo;debito;credito;saldo_acumulado";

    [Fact]
    public void Builds_excel_friendly_csv()
    {
        var ccId = Guid.NewGuid();
        var movements = new List<GeneralLedgerMovement>
        {
            new(Guid.NewGuid(), 10, new DateOnly(2026, 7, 5), "Pagamento; parcial", "NF-1", ccId, 1234.56m, 0m, 1234.56m),
            new(Guid.NewGuid(), 11, new DateOnly(2026, 7, 10), "=CMD()", null, null, 0m, 34.5m, 1200.06m),
        };
        var account = new GeneralLedgerAccount(
            Guid.NewGuid(), "1.1.01", "R123", "Caixa", NormalBalance.Debit,
            100m, movements, 1234.56m, 34.5m, 1200.06m);

        var csv = GeneralLedgerCsv.Build([account], new Dictionary<Guid, string> { [ccId] = "CC01 — Admin" });

        Assert.StartsWith(Header + "\r\n", csv);
        Assert.Contains("1.1.01;R123;Caixa;;Saldo de abertura;;;;;100,00", csv); // opening row
        Assert.Contains("1234,56", csv);                 // comma decimals
        Assert.Contains("\"Pagamento; parcial\"", csv);   // quoted (contains ';')
        Assert.Contains("'=CMD()", csv);                  // CSV formula-injection guard
        Assert.Contains("CC01 — Admin", csv);             // resolved cost-center label
    }

    [Fact]
    public void Empty_accounts_yields_header_only() =>
        Assert.Equal(Header + "\r\n", GeneralLedgerCsv.Build([], new Dictionary<Guid, string>()));
}
