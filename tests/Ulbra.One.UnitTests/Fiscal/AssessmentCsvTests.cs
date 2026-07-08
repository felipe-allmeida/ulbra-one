using Ulbra.One.Finance.Domain;
using Ulbra.One.Fiscal.Domain.Assessment;
using Xunit;

namespace Ulbra.One.UnitTests.Fiscal;

public class AssessmentCsvTests
{
    private static WithholdingFact Fact(
        WithholdingTaxType tax = WithholdingTaxType.Irrf,
        decimal baseAmount = 10000m,
        decimal amount = 150m,
        string company = "Empresa A",
        string cnpj = "11222333000181",
        string costCenter = "Marketing",
        string counterparty = "Fornecedor X",
        string? document = "NF-1") =>
        new(Guid.NewGuid(), counterparty, document, Guid.NewGuid(), company, cnpj,
            Guid.NewGuid(), "CC", costCenter, Guid.NewGuid(), "99888777000166",
            tax, baseAmount, amount, baseAmount, new DateOnly(2026, 7, 3));

    [Fact]
    public void First_line_is_the_header()
    {
        var csv = AssessmentCsv.Build("2026-07", []);
        var firstLine = csv.Split('\n')[0].TrimEnd('\r');
        Assert.Equal("competencia;empresa;cnpj;centro_custo;fornecedor;documento;guia;tributo;base;retido", firstLine);
    }

    [Fact]
    public void Empty_facts_yield_only_the_header()
    {
        var csv = AssessmentCsv.Build("2026-07", []);
        var lines = csv.Split('\n').Where(l => l.Trim().Length > 0).ToArray();
        Assert.Single(lines);
    }

    [Fact]
    public void Row_has_fields_in_order_with_comma_decimals()
    {
        var csv = AssessmentCsv.Build("2026-07", [Fact(WithholdingTaxType.Cofins, 1000m, 30m)]);
        var row = csv.Split('\n')[1].TrimEnd('\r');
        // competencia;empresa;cnpj;centro_custo;fornecedor;documento;guia;tributo;base;retido
        Assert.Equal("2026-07;Empresa A;11222333000181;Marketing;Fornecedor X;NF-1;Csrf;Cofins;1000,00;30,00", row);
    }

    [Fact]
    public void Fields_with_separator_or_quotes_are_escaped()
    {
        var csv = AssessmentCsv.Build("2026-07", [Fact(counterparty: "Silva; Souza \"ME\"", document: null)]);
        var row = csv.Split('\n')[1].TrimEnd('\r');
        Assert.Contains("\"Silva; Souza \"\"ME\"\"\"", row);
        // null document → empty field between the two semicolons
        Assert.Contains(";;", row);
    }

    [Fact]
    public void Fields_starting_with_a_formula_char_are_defanged()
    {
        // A name/document beginning with = + - @ must not be evaluated as a formula in Excel/Sheets.
        var csv = AssessmentCsv.Build(
            "2026-07", [Fact(company: "+SUM(A1)", counterparty: "=HYPERLINK(\"http://x\",\"y\")", document: "@cmd")]);
        var row = csv.Split('\n')[1].TrimEnd('\r');
        Assert.Contains("'+SUM(A1)", row); // defanged, no special char → not quoted
        Assert.Contains("'@cmd", row);
        Assert.Contains("\"'=HYPERLINK(", row); // defanged AND quoted (contains a quote)
    }
}
