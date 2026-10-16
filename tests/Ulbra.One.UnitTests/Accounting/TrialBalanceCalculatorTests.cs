using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Accounting.Domain;
using Xunit;

namespace Ulbra.One.UnitTests.Accounting;

public class TrialBalanceCalculatorTests
{
    private static readonly Guid Cash = Guid.NewGuid();
    private static readonly Guid Revenue = Guid.NewGuid();
    private static readonly DateOnly D = new(2026, 6, 1);

    private static PostedLine Line(Guid acct, string code, AccountGroup group, NormalBalance nb, EntrySide side, decimal amount) =>
        new(acct, code, code, group, nb, side, amount, D);

    private static PostedLine LineOn(Guid acct, string code, AccountGroup group, NormalBalance nb, EntrySide side, decimal amount, DateOnly date) =>
        new(acct, code, code, group, nb, side, amount, date);

    [Fact]
    public void Empty_input_yields_balanced_empty_report()
    {
        var tb = TrialBalanceCalculator.Build([]);
        Assert.Empty(tb.Rows);
        Assert.Equal(0m, tb.TotalDebit);
        Assert.Equal(0m, tb.TotalCredit);
        Assert.True(tb.IsBalanced);
    }

    [Fact]
    public void Aggregates_per_account_and_respects_normal_balance()
    {
        var lines = new[]
        {
            Line(Cash, "1.1", AccountGroup.Asset, NormalBalance.Debit, EntrySide.Debit, 300m),
            Line(Cash, "1.1", AccountGroup.Asset, NormalBalance.Debit, EntrySide.Credit, 100m),
            Line(Revenue, "3.1", AccountGroup.Revenue, NormalBalance.Credit, EntrySide.Credit, 200m),
        };

        var tb = TrialBalanceCalculator.Build(lines);

        var cash = Assert.Single(tb.Rows, r => r.LedgerAccountId == Cash);
        Assert.Equal(300m, cash.TotalDebit);
        Assert.Equal(100m, cash.TotalCredit);
        Assert.Equal(200m, cash.Balance); // debit-normal: 300 - 100

        var rev = Assert.Single(tb.Rows, r => r.LedgerAccountId == Revenue);
        Assert.Equal(200m, rev.Balance); // credit-normal: 200 - 0
    }

    [Fact]
    public void Grand_totals_balance_for_double_entry_input()
    {
        var lines = new[]
        {
            Line(Cash, "1.1", AccountGroup.Asset, NormalBalance.Debit, EntrySide.Debit, 500m),
            Line(Revenue, "3.1", AccountGroup.Revenue, NormalBalance.Credit, EntrySide.Credit, 500m),
        };

        var tb = TrialBalanceCalculator.Build(lines);

        Assert.Equal(500m, tb.TotalDebit);
        Assert.Equal(500m, tb.TotalCredit);
        Assert.True(tb.IsBalanced);
    }

    [Fact]
    public void Rows_ordered_by_account_code()
    {
        var lines = new[]
        {
            Line(Revenue, "3.1", AccountGroup.Revenue, NormalBalance.Credit, EntrySide.Credit, 10m),
            Line(Cash, "1.1", AccountGroup.Asset, NormalBalance.Debit, EntrySide.Debit, 10m),
        };
        var tb = TrialBalanceCalculator.Build(lines);
        Assert.Equal("1.1", tb.Rows[0].AccountCode);
        Assert.Equal("3.1", tb.Rows[1].AccountCode);
    }

    // --- Period columns (AEL-726) ---

    private static readonly DateOnly Jan = new(2026, 1, 15);
    private static readonly DateOnly Feb = new(2026, 2, 15);
    private static readonly DateOnly Mar = new(2026, 3, 15);

    [Fact] // U1: no range ≡ all-time; opening is 0 and Balance is the accumulated balance (backward compat)
    public void No_range_equals_all_time_with_zero_opening()
    {
        var lines = new[]
        {
            LineOn(Cash, "1.1", AccountGroup.Asset, NormalBalance.Debit, EntrySide.Debit, 300m, Jan),
            LineOn(Cash, "1.1", AccountGroup.Asset, NormalBalance.Debit, EntrySide.Credit, 100m, Feb),
        };

        var open = TrialBalanceCalculator.Build(lines);
        var explicitNull = TrialBalanceCalculator.Build(lines, null, null);

        var a = Assert.Single(open.Rows);
        Assert.Equal(0m, a.OpeningBalance);
        Assert.Equal(300m, a.TotalDebit);
        Assert.Equal(100m, a.TotalCredit);
        Assert.Equal(200m, a.Balance);
        Assert.Equal(open.Rows[0], explicitNull.Rows[0]);
    }

    [Fact] // U2: an account whose movement is entirely before `from` shows only an opening balance
    public void Opening_only_account_has_no_period_movement()
    {
        var lines = new[]
        {
            LineOn(Cash, "1.1", AccountGroup.Asset, NormalBalance.Debit, EntrySide.Debit, 500m, Jan),
        };

        var tb = TrialBalanceCalculator.Build(lines, from: Feb, to: Mar);

        var a = Assert.Single(tb.Rows);
        Assert.Equal(500m, a.OpeningBalance);
        Assert.Equal(0m, a.TotalDebit);
        Assert.Equal(0m, a.TotalCredit);
        Assert.Equal(500m, a.Balance); // closing = opening + 0
    }

    [Fact] // U3: closing = opening + signed(debit - credit) for every account
    public void Closing_equals_opening_plus_signed_movement()
    {
        var lines = new[]
        {
            LineOn(Cash, "1.1", AccountGroup.Asset, NormalBalance.Debit, EntrySide.Debit, 500m, Jan),   // opening
            LineOn(Cash, "1.1", AccountGroup.Asset, NormalBalance.Debit, EntrySide.Debit, 200m, Feb),   // period
            LineOn(Cash, "1.1", AccountGroup.Asset, NormalBalance.Debit, EntrySide.Credit, 50m, Feb),   // period
        };

        var a = Assert.Single(TrialBalanceCalculator.Build(lines, from: Feb, to: Mar).Rows);
        Assert.Equal(500m, a.OpeningBalance);
        Assert.Equal(200m, a.TotalDebit);
        Assert.Equal(50m, a.TotalCredit);
        var signedMovement = a.NormalBalance == NormalBalance.Debit ? a.TotalDebit - a.TotalCredit : a.TotalCredit - a.TotalDebit;
        Assert.Equal(a.OpeningBalance + signedMovement, a.Balance); // 500 + 150 = 650
        Assert.Equal(650m, a.Balance);
    }

    [Fact] // U4: movement strictly after `to` is excluded (account absent when it has nothing up to `to`)
    public void Movement_after_to_is_excluded()
    {
        var lines = new[]
        {
            LineOn(Cash, "1.1", AccountGroup.Asset, NormalBalance.Debit, EntrySide.Debit, 500m, Mar),
        };

        var tb = TrialBalanceCalculator.Build(lines, from: Jan, to: Feb);
        Assert.Empty(tb.Rows);
    }

    [Fact] // U5: boundaries — from-1 = opening, from & to = period, to+1 = excluded
    public void Date_boundaries_bucket_correctly()
    {
        var from = new DateOnly(2026, 2, 1);
        var to = new DateOnly(2026, 2, 28);
        var lines = new[]
        {
            LineOn(Cash, "1.1", AccountGroup.Asset, NormalBalance.Debit, EntrySide.Debit, 10m, new(2026, 1, 31)),  // opening
            LineOn(Cash, "1.1", AccountGroup.Asset, NormalBalance.Debit, EntrySide.Debit, 20m, from),              // period
            LineOn(Cash, "1.1", AccountGroup.Asset, NormalBalance.Debit, EntrySide.Debit, 40m, to),               // period
            LineOn(Cash, "1.1", AccountGroup.Asset, NormalBalance.Debit, EntrySide.Debit, 80m, new(2026, 3, 1)),  // excluded
        };

        var a = Assert.Single(TrialBalanceCalculator.Build(lines, from, to).Rows);
        Assert.Equal(10m, a.OpeningBalance);
        Assert.Equal(60m, a.TotalDebit); // 20 + 40
        Assert.Equal(70m, a.Balance);    // 10 + 60
    }

    [Fact] // U6: a debit-normal account with a credit opening carries a negative opening that a later debit flips
    public void Opening_and_closing_can_have_opposite_signs()
    {
        var lines = new[]
        {
            LineOn(Cash, "1.1", AccountGroup.Asset, NormalBalance.Debit, EntrySide.Credit, 100m, Jan), // opening −100
            LineOn(Cash, "1.1", AccountGroup.Asset, NormalBalance.Debit, EntrySide.Debit, 300m, Feb),  // period +300
        };

        var a = Assert.Single(TrialBalanceCalculator.Build(lines, from: Feb, to: Mar).Rows);
        Assert.Equal(-100m, a.OpeningBalance);
        Assert.Equal(200m, a.Balance);
    }

    [Fact] // U7: to < from is rejected
    public void Inverted_range_throws()
    {
        Assert.Throws<ArgumentException>(() => TrialBalanceCalculator.Build([], from: Mar, to: Jan));
    }

    [Fact] // U10: original + reversal mirror net to zero once both are in range; earlier window keeps the original
    public void Reversal_nets_to_zero_and_preserves_earlier_window()
    {
        // Original in Jan (Reversed entry, still loaded) + its mirror in Feb (the reversal, Posted).
        var lines = new[]
        {
            LineOn(Cash, "1.1", AccountGroup.Asset, NormalBalance.Debit, EntrySide.Debit, 500m, Jan),
            LineOn(Cash, "1.1", AccountGroup.Asset, NormalBalance.Debit, EntrySide.Credit, 500m, Feb),
        };

        // Window up to Jan: only the original → still 500 (immutability of the earlier period).
        var jan = Assert.Single(TrialBalanceCalculator.Build(lines, to: new DateOnly(2026, 1, 31)).Rows);
        Assert.Equal(500m, jan.Balance);

        // All-time: original + mirror net to 0.
        var all = Assert.Single(TrialBalanceCalculator.Build(lines).Rows);
        Assert.Equal(0m, all.Balance);
    }
}
