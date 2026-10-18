using Ulbra.One.Accounting.Domain;

namespace Ulbra.One.Accounting.Application.Reports;

/// <summary>An account line in the balance sheet (BP), carrying its balance as of the cut-off date.</summary>
public sealed record BalanceSheetLine(
    Guid LedgerAccountId,
    string AccountCode,
    string AccountName,
    AccountGroup AccountGroup,
    decimal Balance);

/// <summary>The balance sheet (Balanço Patrimonial) as of a date. Equity includes the period result line
/// (lucro/prejuízo do exercício) so the accounting identity closes.</summary>
public sealed record BalanceSheet(
    DateOnly AsOf,
    IReadOnlyList<BalanceSheetLine> Assets,
    IReadOnlyList<BalanceSheetLine> Liabilities,
    IReadOnlyList<BalanceSheetLine> Equity,
    decimal PeriodResult,
    decimal TotalAssets,
    decimal TotalLiabilities,
    decimal TotalEquity)
{
    /// <summary>Fundamental identity: Ativo = Passivo + Patrimônio Líquido (PL includes the period result).</summary>
    public bool IsBalanced => TotalAssets == TotalLiabilities + TotalEquity;
}

/// <summary>Pure calculator producing a balance sheet from posted lines up to a cut-off date. No EF, no I/O.</summary>
public static class BalanceSheetCalculator
{
    /// <summary>Synthetic id/code used for the "resultado do exercício" equity line that folds Revenue−Expense.</summary>
    public static readonly Guid PeriodResultAccountId = Guid.Empty;
    public const string PeriodResultCode = "RESULT";

    public static BalanceSheet Build(IEnumerable<PostedLine> lines, DateOnly asOf,
        string periodResultName = "Resultado do Exercício")
    {
        ArgumentNullException.ThrowIfNull(lines);

        var upTo = lines.Where(l => l.CompetenceDate <= asOf).ToList();

        var byAccount = upTo
            .GroupBy(l => l.LedgerAccountId)
            .Select(g =>
            {
                var first = g.First();
                var balance = g.Sum(l => AccountBalance.Signed(l.NormalBalance, l.Side, l.Amount));
                return new BalanceSheetLine(first.LedgerAccountId, first.AccountCode, first.AccountName,
                    first.AccountGroup, balance);
            })
            .OrderBy(l => l.AccountCode, StringComparer.Ordinal)
            .ToList();

        var assets = byAccount.Where(l => l.AccountGroup == AccountGroup.Asset).ToList();
        var liabilities = byAccount.Where(l => l.AccountGroup == AccountGroup.Liability).ToList();
        var equity = byAccount.Where(l => l.AccountGroup == AccountGroup.Equity).ToList();

        // Result of the open period = Revenue − Expense, accumulated up to the cut-off. It is not yet
        // closed into an equity account, so we surface it as an explicit equity line so PL closes.
        var revenue = byAccount.Where(l => l.AccountGroup == AccountGroup.Revenue).Sum(l => l.Balance);
        var expense = byAccount.Where(l => l.AccountGroup == AccountGroup.Expense).Sum(l => l.Balance);
        var periodResult = revenue - expense;

        var equityWithResult = new List<BalanceSheetLine>(equity)
        {
            new(PeriodResultAccountId, PeriodResultCode, periodResultName, AccountGroup.Equity, periodResult),
        };

        return new BalanceSheet(asOf, assets, liabilities, equityWithResult, periodResult,
            assets.Sum(l => l.Balance),
            liabilities.Sum(l => l.Balance),
            equityWithResult.Sum(l => l.Balance));
    }
}
