using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Common.Application.Ids;

namespace Ulbra.One.Accounting.Application.Contracts;

/// <summary>An account line in the balance sheet (Balanço Patrimonial). The synthetic period-result
/// equity line carries a null id (it is not a persisted ledger account).</summary>
public sealed record BalanceSheetLineResponse(
    LedgerAccountId? LedgerAccountId,
    string AccountCode,
    string AccountName,
    AccountGroup AccountGroup,
    decimal Balance)
{
    public static BalanceSheetLineResponse From(BalanceSheetLine l) => new(
        l.LedgerAccountId == BalanceSheetCalculator.PeriodResultAccountId
            ? null
            : new LedgerAccountId(l.LedgerAccountId),
        l.AccountCode, l.AccountName, l.AccountGroup, l.Balance);
}

/// <summary>The balance sheet (Balanço Patrimonial) as of a date, consolidated or per-company.</summary>
public sealed record BalanceSheetResponse(
    CompanyId? CompanyId,
    DateOnly AsOf,
    IReadOnlyList<BalanceSheetLineResponse> Assets,
    IReadOnlyList<BalanceSheetLineResponse> Liabilities,
    IReadOnlyList<BalanceSheetLineResponse> Equity,
    decimal PeriodResult,
    decimal TotalAssets,
    decimal TotalLiabilities,
    decimal TotalEquity,
    bool IsBalanced)
{
    public static BalanceSheetResponse From(BalanceSheet bs, CompanyId? companyId) => new(
        companyId,
        bs.AsOf,
        bs.Assets.Select(BalanceSheetLineResponse.From).ToList(),
        bs.Liabilities.Select(BalanceSheetLineResponse.From).ToList(),
        bs.Equity.Select(BalanceSheetLineResponse.From).ToList(),
        bs.PeriodResult,
        bs.TotalAssets,
        bs.TotalLiabilities,
        bs.TotalEquity,
        bs.IsBalanced);
}
