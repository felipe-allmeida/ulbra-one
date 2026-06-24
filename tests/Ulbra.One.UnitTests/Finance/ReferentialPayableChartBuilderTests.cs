using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Finance.Application.Seeding;

namespace Ulbra.One.UnitTests.Finance;

public class ReferentialPayableChartBuilderTests
{
    // Expense subtree fragment (already filtered to "4.02*"); "4.02"'s real parent "4" is excluded.
    private static IReadOnlyList<ReferentialPayableRow> Fixture() =>
    [
        new("4.02", "DESPESAS", true, "4"),
        new("4.02.02", "DESPESAS ADMINISTRATIVAS", true, "4.02"),
        new("4.02.02.01", "DESPESAS ADMINISTRATIVAS", true, "4.02.02"),
        new("4.02.02.01.05", "Despesas de Consumo (Água/Esgoto/Energia Elétrica/Comunicação)", false, "4.02.02.01"),
    ];

    private static ChartAccount Get(IReadOnlyList<ChartAccount> a, string code) => a.Single(x => x.Code == code);

    [Fact]
    public void Builds_accounting_chart_with_kind_and_nature()
    {
        var chart = ReferentialPayableChartBuilder.BuildAccounting(Fixture());
        Assert.All(chart, c => Assert.Equal(ChartType.Accounting, c.ChartType));
        Assert.Equal(AccountKind.Synthetic, Get(chart, "4.02").Kind);
        var leaf = Get(chart, "4.02.02.01.05");
        Assert.Equal(AccountKind.Analytical, leaf.Kind);
        Assert.Equal(AccountNature.Debit, leaf.Nature);
        Assert.True(leaf.CanReceivePayable);
    }

    [Fact]
    public void Wires_parents_within_the_subset_and_roots_the_top()
    {
        var chart = ReferentialPayableChartBuilder.BuildAccounting(Fixture());
        Assert.Null(Get(chart, "4.02").ParentId);                       // parent "4" outside subset → root
        Assert.Equal(Get(chart, "4.02").Id, Get(chart, "4.02.02").ParentId);
        Assert.Equal(Get(chart, "4.02.02.01").Id, Get(chart, "4.02.02.01.05").ParentId);
    }
}
