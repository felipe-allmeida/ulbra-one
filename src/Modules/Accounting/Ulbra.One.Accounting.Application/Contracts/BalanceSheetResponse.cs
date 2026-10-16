using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Accounting.Application.Contracts;

/// <summary>An account line in the balance sheet (Balanço Patrimonial). The synthetic period-result
/// equity line carries a null id (it is not a persisted ledger account).</summary>
[SwaggerSchema("One account line of the balance sheet.")]
public sealed record BalanceSheetLineResponse(
    [property: SwaggerSchema("The account behind the line. Null on the synthetic period-result line in "
        + "equity, which is computed rather than posted — so do not expect to fetch it.")]
    LedgerAccountId? LedgerAccountId,
    [property: SwaggerSchema("Hierarchical account code.")]
    [property: SwaggerExample("1.1.1.01")]
    string AccountCode,
    [property: SwaggerSchema("Account name.")]
    [property: SwaggerExample("Caixa e equivalentes")]
    string AccountName,
    [property: SwaggerSchema("Which section the line belongs to.")]
    AccountGroup AccountGroup,
    [property: SwaggerSchema("Balance as of the report date, in BRL, signed by the account's normal "
        + "balance.")]
    [property: SwaggerExample("482350.00")]
    decimal Balance)
{
    public static BalanceSheetLineResponse From(BalanceSheetLine l) => new(
        l.LedgerAccountId == BalanceSheetCalculator.PeriodResultAccountId
            ? null
            : new LedgerAccountId(l.LedgerAccountId),
        l.AccountCode, l.AccountName, l.AccountGroup, l.Balance);
}

/// <summary>The balance sheet (Balanço Patrimonial) as of a date, consolidated or per-company.</summary>
[SwaggerSchema("The balance sheet (Balanço Patrimonial) at a point in time. The period result is folded "
    + "into equity as a synthetic line, which is what makes the two sides tie before the year-end close "
    + "has been run.")]
public sealed record BalanceSheetResponse(
    [property: SwaggerSchema("Company the report covers. Null means consolidated.")]
    CompanyId? CompanyId,
    [property: SwaggerSchema("The date the balances are taken at. A balance sheet is a snapshot, not a "
        + "range.")]
    [property: SwaggerExample("2026-07-31")]
    DateOnly AsOf,
    [property: SwaggerSchema("Asset lines (ativo).")]
    IReadOnlyList<BalanceSheetLineResponse> Assets,
    [property: SwaggerSchema("Liability lines (passivo).")]
    IReadOnlyList<BalanceSheetLineResponse> Liabilities,
    [property: SwaggerSchema("Equity lines (patrimônio líquido), including the synthetic period-result "
        + "line.")]
    IReadOnlyList<BalanceSheetLineResponse> Equity,
    [property: SwaggerSchema("Accumulated result of the period, in BRL — positive is profit. It is "
        + "already counted inside TotalEquity; adding it again double-counts.")]
    [property: SwaggerExample("184200.00")]
    decimal PeriodResult,
    [property: SwaggerSchema("Total assets, in BRL.")]
    [property: SwaggerExample("2480000.00")]
    decimal TotalAssets,
    [property: SwaggerSchema("Total liabilities, in BRL.")]
    [property: SwaggerExample("1120000.00")]
    decimal TotalLiabilities,
    [property: SwaggerSchema("Total equity, in BRL, with the period result included.")]
    [property: SwaggerExample("1360000.00")]
    decimal TotalEquity,
    [property: SwaggerSchema("Whether assets equal liabilities plus equity. False points at an "
        + "inconsistency in the ledger, not at a rounding difference.")]
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
