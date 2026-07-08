using Ulbra.One.Accounting.Application.Seeding;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;

namespace Ulbra.One.UnitTests.Accounting;

/// <summary>The AELBRA "Modelo de Planos" is the single canonical chart of accounts. These tests guard its
/// integrity and that the codes the dev seeder + integration tests post to exist and are postable.</summary>
public class AelbraChartTests
{
    private static readonly IReadOnlyList<LedgerAccount> Chart = AelbraChart.Build();

    [Fact]
    public void Builds_the_full_chart()
    {
        Assert.Equal(1119, Chart.Count);
        Assert.Equal(177, Chart.Count(a => a.Kind == LedgerAccountKind.Synthetic));
        Assert.Equal(942, Chart.Count(a => a.Kind == LedgerAccountKind.Analytical));
    }

    [Fact]
    public void Codes_and_reduced_codes_are_unique()
    {
        Assert.Equal(Chart.Count, Chart.Select(a => a.Code).Distinct().Count());
        // Every account carries a reduced code, all distinct.
        Assert.All(Chart, a => Assert.False(string.IsNullOrWhiteSpace(a.ReducedCode)));
        Assert.Equal(Chart.Count, Chart.Select(a => a.ReducedCode).Distinct().Count());
    }

    [Fact]
    public void Has_the_five_roots_as_synthetic_group_heads()
    {
        (string code, AccountGroup group)[] roots =
        [
            ("1", AccountGroup.Asset), ("2", AccountGroup.Liability), ("3", AccountGroup.Revenue),
            ("4", AccountGroup.Expense), ("5", AccountGroup.Expense),
        ];
        foreach (var (code, group) in roots)
        {
            var root = Chart.Single(a => a.Code == code);
            Assert.Null(root.ParentId);
            Assert.Equal(LedgerAccountKind.Synthetic, root.Kind);
            Assert.Equal(group, root.AccountGroup);
        }
    }

    [Fact]
    public void All_five_account_groups_are_represented()
    {
        foreach (var g in new[] { AccountGroup.Asset, AccountGroup.Liability, AccountGroup.Equity, AccountGroup.Revenue, AccountGroup.Expense })
            Assert.Contains(Chart, a => a.AccountGroup == g);
    }

    [Fact]
    public void Patrimonio_liquido_subtree_is_equity_rooted_at_2_6()
    {
        var pl = Chart.Single(a => a.Code == "2.6");
        Assert.Equal(AccountGroup.Equity, pl.AccountGroup);
        Assert.Null(pl.ParentId); // code-parent "2" is Liability → detached to an Equity root
        // its descendants stay Equity
        Assert.All(Chart.Where(a => a.Code.StartsWith("2.6.", StringComparison.Ordinal)),
            a => Assert.Equal(AccountGroup.Equity, a.AccountGroup));
    }

    [Fact]
    public void Every_wired_parent_is_a_same_group_synthetic()
    {
        var byId = Chart.ToDictionary(a => a.Id);
        foreach (var a in Chart.Where(a => a.ParentId is not null))
        {
            var parent = byId[a.ParentId!.Value];
            Assert.Equal(LedgerAccountKind.Synthetic, parent.Kind);
            Assert.Equal(a.AccountGroup, parent.AccountGroup);
        }
    }

    [Fact]
    public void Normal_balance_comes_from_the_sheet_so_contra_accounts_are_preserved()
    {
        // Asset with a credit normal balance (contra-asset) and Liability with a debit balance exist,
        // which only holds if NormalBalance is read from the Natureza column, not derived from the group.
        Assert.Contains(Chart, a => a.AccountGroup == AccountGroup.Asset && a.NormalBalance == NormalBalance.Credit);
        Assert.Contains(Chart, a => a.AccountGroup == AccountGroup.Liability && a.NormalBalance == NormalBalance.Debit);
    }

    [Fact]
    public void Contains_the_analytical_codes_the_dev_seeder_and_integration_tests_post_to()
    {
        // Guards DevDataSeeder + the accounting integration tests against KeyNotFoundException:
        // every code they post journal lines to / classify payables with must exist and be analytical.
        var byCode = Chart.ToDictionary(a => a.Code);
        string[] seederCodes =
        [
            // DevDataSeeder journal entries + payable classification
            "1.2.05.05.020", "1.2.05.15.015", "2.6.05.05.020", "3.2.05.10.020",
            "4.2.05.05.020", "4.2.15.05.020", "4.2.15.10.200",
            // AccountingModule posting defaults (fornecedores + withholding retention accounts)
            "2.2.05.15.020", "2.2.05.30.020", "2.2.05.30.040", "2.2.05.30.060", "2.2.05.30.080",
        ];
        foreach (var code in seederCodes)
        {
            Assert.True(byCode.ContainsKey(code), $"Seeder code missing from chart: {code}");
            Assert.Equal(LedgerAccountKind.Analytical, byCode[code].Kind);
        }
    }
}
