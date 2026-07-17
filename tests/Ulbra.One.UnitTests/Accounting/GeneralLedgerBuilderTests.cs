using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Accounting.Domain;
using Xunit;

namespace Ulbra.One.UnitTests.Accounting;

public class GeneralLedgerBuilderTests
{
    private static GeneralLedgerPostedLine Line(
        Guid accountId, NormalBalance nb, DateOnly competence, EntrySide side, decimal amount,
        int number = 1, string code = "1.1.01") =>
        new(accountId, code, "R" + code, "Account " + code, nb,
            Guid.NewGuid(), number, competence, "hist", "doc", side, amount, null, null);

    [Fact]
    public void Debit_normal_account_opening_and_running_balance()
    {
        var acct = Guid.NewGuid();
        var rows = new[]
        {
            // Before `from`: opening = 1000 - 200 = 800.
            Line(acct, NormalBalance.Debit, new DateOnly(2026, 6, 10), EntrySide.Debit, 1000m, 1),
            Line(acct, NormalBalance.Debit, new DateOnly(2026, 6, 20), EntrySide.Credit, 200m, 2),
            // In range.
            Line(acct, NormalBalance.Debit, new DateOnly(2026, 7, 5), EntrySide.Debit, 500m, 3),
            Line(acct, NormalBalance.Debit, new DateOnly(2026, 7, 12), EntrySide.Credit, 300m, 4),
        };

        var a = Assert.Single(GeneralLedgerBuilder.Build(rows, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)));

        Assert.Equal(800m, a.OpeningBalance);
        Assert.Equal(2, a.Movements.Count);
        Assert.Equal(1300m, a.Movements[0].AccumulatedBalance); // +500
        Assert.Equal(1000m, a.Movements[1].AccumulatedBalance); // -300
        Assert.Equal(500m, a.TotalDebit);
        Assert.Equal(300m, a.TotalCredit);
        Assert.Equal(1000m, a.ClosingBalance);
        Assert.Equal(500m, a.Movements[0].Debit);
        Assert.Equal(0m, a.Movements[0].Credit);
    }

    [Fact]
    public void Credit_normal_account_signs_are_symmetric()
    {
        var acct = Guid.NewGuid();
        var rows = new[]
        {
            Line(acct, NormalBalance.Credit, new DateOnly(2026, 6, 1), EntrySide.Credit, 1000m, 1),
            Line(acct, NormalBalance.Credit, new DateOnly(2026, 6, 2), EntrySide.Debit, 400m, 2),
            Line(acct, NormalBalance.Credit, new DateOnly(2026, 7, 3), EntrySide.Credit, 200m, 3),
            Line(acct, NormalBalance.Credit, new DateOnly(2026, 7, 18), EntrySide.Debit, 500m, 4),
        };

        var a = Assert.Single(GeneralLedgerBuilder.Build(rows, new DateOnly(2026, 7, 1), null));

        Assert.Equal(600m, a.OpeningBalance);                  // 1000 - 400
        Assert.Equal(800m, a.Movements[0].AccumulatedBalance); // +200 (credit raises a credit-normal account)
        Assert.Equal(300m, a.Movements[1].AccumulatedBalance); // -500 (debit lowers it)
        Assert.Equal(300m, a.ClosingBalance);
    }

    [Fact]
    public void Orders_movements_by_competence_then_number_then_side()
    {
        var acct = Guid.NewGuid();
        var rows = new[]
        {
            Line(acct, NormalBalance.Debit, new DateOnly(2026, 7, 10), EntrySide.Credit, 50m, 9),
            Line(acct, NormalBalance.Debit, new DateOnly(2026, 7, 5), EntrySide.Credit, 20m, 2),
            Line(acct, NormalBalance.Debit, new DateOnly(2026, 7, 5), EntrySide.Debit, 100m, 2),
        };

        var a = Assert.Single(GeneralLedgerBuilder.Build(rows, new DateOnly(2026, 7, 1), null));

        // jul-05 debit (100) before jul-05 credit (20) before jul-10 credit (50).
        Assert.Equal(100m, a.Movements[0].Debit);
        Assert.Equal(80m, a.Movements[1].AccumulatedBalance); // 100 - 20
        Assert.Equal(30m, a.Movements[2].AccumulatedBalance); // 80 - 50
    }

    [Fact]
    public void Opening_is_zero_without_prior_movements()
    {
        var acct = Guid.NewGuid();
        var rows = new[] { Line(acct, NormalBalance.Debit, new DateOnly(2026, 7, 5), EntrySide.Debit, 100m) };

        var a = Assert.Single(GeneralLedgerBuilder.Build(rows, new DateOnly(2026, 7, 1), null));

        Assert.Equal(0m, a.OpeningBalance);
        Assert.Equal(100m, a.ClosingBalance);
    }

    [Fact]
    public void Account_with_only_opening_has_no_movements()
    {
        var acct = Guid.NewGuid();
        var rows = new[] { Line(acct, NormalBalance.Debit, new DateOnly(2026, 6, 5), EntrySide.Debit, 100m) };

        var a = Assert.Single(GeneralLedgerBuilder.Build(rows, new DateOnly(2026, 7, 1), null));

        Assert.Equal(100m, a.OpeningBalance);
        Assert.Empty(a.Movements);
        Assert.Equal(100m, a.ClosingBalance);
        Assert.Equal(0m, a.TotalDebit);
    }

    [Fact]
    public void Null_from_gives_zero_opening_and_all_movements()
    {
        var acct = Guid.NewGuid();
        var rows = new[]
        {
            Line(acct, NormalBalance.Debit, new DateOnly(2026, 6, 5), EntrySide.Debit, 100m, 1),
            Line(acct, NormalBalance.Debit, new DateOnly(2026, 7, 5), EntrySide.Debit, 50m, 2),
        };

        var a = Assert.Single(GeneralLedgerBuilder.Build(rows, null, null));

        Assert.Equal(0m, a.OpeningBalance);
        Assert.Equal(2, a.Movements.Count);
        Assert.Equal(150m, a.ClosingBalance);
    }

    [Fact]
    public void Orders_accounts_by_code()
    {
        var liability = Line(Guid.NewGuid(), NormalBalance.Credit, new DateOnly(2026, 7, 1), EntrySide.Credit, 10m, 1, "2.1.01");
        var asset = Line(Guid.NewGuid(), NormalBalance.Debit, new DateOnly(2026, 7, 1), EntrySide.Debit, 10m, 1, "1.1.01");

        var result = GeneralLedgerBuilder.Build(new[] { liability, asset }, null, null);

        Assert.Equal("1.1.01", result[0].AccountCode);
        Assert.Equal("2.1.01", result[1].AccountCode);
    }

    [Fact]
    public void Rejects_inverted_range() =>
        Assert.Throws<ArgumentException>(() =>
            GeneralLedgerBuilder.Build([], new DateOnly(2026, 7, 31), new DateOnly(2026, 7, 1)));

    [Fact]
    public void Empty_input_yields_no_accounts() =>
        Assert.Empty(GeneralLedgerBuilder.Build([], null, null));
}
