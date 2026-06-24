using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Accounting.Application.Seeding;

namespace Ulbra.One.UnitTests.Accounting;

public class ReferentialChartImporterTests
{
    private static readonly Guid Company = Guid.NewGuid();

    // Mini fixture exercising: Asset leaf, PL nested under Passivo, result split.
    private static IReadOnlyList<ReferentialAccountRow> Fixture() =>
    [
        new("1", "ATIVO", "S", null, 1, "1"),
        new("1.01", "CAIXA", "A", "1", 2, "1"),
        new("2", "PASSIVO", "S", null, 1, "2"),
        new("2.05", "PATRIMÔNIO LÍQUIDO SOCIAL", "S", "2", 2, "3"),
        new("2.05.01", "PATRIMÔNIO SOCIAL", "A", "2.05", 3, "3"),
        new("4", "SUPERÁVIT/DÉFICIT", "S", null, 1, "4"),
        new("4.01", "RECEITAS", "S", "4", 2, "4"),
        new("4.01.01", "RECEITA DE DOAÇÕES", "A", "4.01", 3, "4"),
        new("4.02", "DESPESAS", "S", "4", 2, "4"),
        new("4.02.01", "DESPESA COM PESSOAL", "A", "4.02", 3, "4"),
    ];

    private static LedgerAccount Get(IReadOnlyList<LedgerAccount> accounts, string code) =>
        accounts.Single(a => a.Code == code);

    [Fact]
    public void Maps_natureza_to_account_group()
    {
        var a = ReferentialChartImporter.Build(Company, Fixture());
        Assert.Equal(AccountGroup.Asset, Get(a, "1").AccountGroup);
        Assert.Equal(AccountGroup.Liability, Get(a, "2").AccountGroup);
        Assert.Equal(AccountGroup.Equity, Get(a, "2.05").AccountGroup);
        Assert.Equal(AccountGroup.Revenue, Get(a, "4.01.01").AccountGroup);
        Assert.Equal(AccountGroup.Expense, Get(a, "4.02.01").AccountGroup);
    }

    [Fact]
    public void Maps_tipo_to_kind()
    {
        var a = ReferentialChartImporter.Build(Company, Fixture());
        Assert.Equal(LedgerAccountKind.Synthetic, Get(a, "1").Kind);
        Assert.Equal(LedgerAccountKind.Analytical, Get(a, "1.01").Kind);
    }

    [Fact]
    public void Wires_parent_only_within_same_group()
    {
        var a = ReferentialChartImporter.Build(Company, Fixture());
        // same-group child wires to parent
        Assert.Equal(Get(a, "1").Id, Get(a, "1.01").ParentId);
        Assert.Equal(Get(a, "2.05").Id, Get(a, "2.05.01").ParentId);
        // Equity (2.05) under Liability (2) → group differs → becomes a root
        Assert.Null(Get(a, "2.05").ParentId);
        // Expense (4.02) under result root "4" (classified Expense) → wires; Revenue (4.01) → root
        Assert.Null(Get(a, "4.01").ParentId);
    }

    [Fact]
    public void Produces_one_account_per_row()
    {
        var a = ReferentialChartImporter.Build(Company, Fixture());
        Assert.Equal(10, a.Count);
        Assert.All(a, x => Assert.Equal(Company, x.CompanyId));
    }
}
