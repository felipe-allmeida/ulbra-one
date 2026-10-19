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
        // 1119 accounts from the AELBRA export + two added in-house: "Apuração do Resultado do Exercício"
        // (2.6.20.05.100) and the unclassified-incurral suspense (1.2.35.20.050).
        Assert.Equal(1121, Chart.Count);
        Assert.Equal(177, Chart.Count(a => a.Kind == LedgerAccountKind.Synthetic));
        Assert.Equal(944, Chart.Count(a => a.Kind == LedgerAccountKind.Analytical));
    }

    [Fact]
    public void Carries_a_postable_suspense_account_for_the_unclassified_incurral()
    {
        // Accounting__UnclassifiedExpenseCode points here. It must be analytical AND active, otherwise
        // CanReceiveEntry is false and the incurral parks — silently restoring the old broken behaviour.
        var suspense = Assert.Single(Chart, a => a.Code == "1.2.35.20.050");
        Assert.Equal(LedgerAccountKind.Analytical, suspense.Kind);
        Assert.True(suspense.IsActive);
        Assert.True(suspense.CanReceiveEntry);
        Assert.Equal(AccountGroup.Asset, suspense.AccountGroup);
        Assert.Equal(NormalBalance.Debit, suspense.NormalBalance);
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
    public void Natureza_is_populated_1_to_5_verbatim_from_the_sheet()
    {
        // Natureza is read straight from the chart, independent of the account's group/code — so a group-1
        // (Asset) account can carry any natureza 1–5. Just assert it's present and in range.
        Assert.All(Chart, a =>
        {
            Assert.NotNull(a.Natureza);
            Assert.InRange(a.Natureza!.Value, 1, 5);
        });
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
    public void Posting_target_codes_are_active_analytical_accounts()
    {
        // Guards DevDataSeeder + the accounting integration tests: every code they post journal lines to /
        // classify payables with must exist AND be postable (analytical + active) — else CanReceiveEntry
        // rejects the line. This catches an account being deactivated by the 2026 active-list filter.
        var byCode = Chart.ToDictionary(a => a.Code);
        string[] postingCodes =
        [
            // DevDataSeeder journal entries + payable classification
            "1.2.05.05.020", "1.2.05.15.015", "2.6.05.05.020", "3.2.05.10.020",
            "4.2.05.05.020", "4.2.15.05.020", "4.2.15.10.200",
            // AccountingModule posting defaults (fornecedores a pagar + withholding retention accounts)
            "2.2.05.15.080", "2.2.05.30.020", "2.2.05.30.040", "2.2.05.30.060", "2.2.05.30.080",
        ];
        foreach (var code in postingCodes)
        {
            Assert.True(byCode.ContainsKey(code), $"Posting code missing from chart: {code}");
            Assert.True(byCode[code].CanReceiveEntry, $"Posting code must be active + analytical: {code}");
        }
    }

    [Fact]
    public void Active_accounts_match_the_2026_movement_list()
    {
        // Accounts with movement/balance in 2026 (aelbra-active-accounts.json, 547 — including the added
        // suspense 1.2.35.20.050) + the added Apuração account (active so closing entries can post) = 548;
        // the remaining 573 stay deactivated.
        Assert.Equal(548, Chart.Count(a => a.IsActive));
        Assert.Equal(573, Chart.Count(a => !a.IsActive));
    }

    [Fact]
    public void Dre_line_is_seeded_on_result_accounts_and_null_elsewhere()
    {
        var byCode = Chart.ToDictionary(a => a.Code);
        // Result accounts (roots 3/4/5) all carry a DRE line; patrimonial accounts (1/2) carry none.
        Assert.All(Chart.Where(a => a.Code[0] is '3' or '4' or '5'), a => Assert.NotNull(a.DreLine));
        Assert.All(Chart.Where(a => a.Code[0] is '1' or '2'), a => Assert.Null(a.DreLine));

        // Spot-check the cascade mapping on real chart codes.
        Assert.Equal(DreLine.GrossRevenue, byCode["3.2.05.10.020"].DreLine);
        Assert.Equal(DreLine.SocialContribution, byCode["4.2.35.05.020"].DreLine); // PROVISAO PARA CSLL
        Assert.Equal(DreLine.IncomeTax, byCode["4.2.35.05.040"].DreLine);          // PROVISAO PARA IRPJ
        Assert.Equal(DreLine.CostOfSales, byCode["5.2.10.05.020"].DreLine);        // CMV
    }

    [Fact]
    public void Apuracao_do_resultado_account_exists_active_and_postable()
    {
        // The year-end closing routine zeroes result accounts into this transitory PL account, then transfers
        // to Lucros/Prejuízos Acumulados — it must be an active, postable (analytical) Equity account.
        var apuracao = Chart.Single(a => a.Code == "2.6.20.05.100");
        Assert.Equal(AccountGroup.Equity, apuracao.AccountGroup);
        Assert.True(apuracao.CanReceiveEntry);
        Assert.Null(apuracao.DreLine); // PL account — not on the DRE
    }
}
