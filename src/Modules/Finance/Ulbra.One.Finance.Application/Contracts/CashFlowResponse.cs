using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Finance.Application.CashFlow;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>Current balance of a bank account in scope, with its reconciliation anchor (if any).</summary>
[SwaggerSchema("One account's contribution to the starting balance, with how trustworthy it is.")]
public sealed record CashFlowAccountBalance(
    BankAccountId BankAccountId,
    [property: SwaggerSchema("Friendly name of the account.")]
    [property: SwaggerExample("Itaú — conta movimento")]
    string Alias,
    [property: SwaggerSchema("Balance the projection starts this account from, in BRL.")]
    [property: SwaggerExample("125000.00")]
    decimal CurrentBalance,
    [property: SwaggerSchema("Date the balance was last reconciled against the bank. Null means it never "
        + "was — the balance is then derived from postings alone, so the whole projection inherits that "
        + "uncertainty.")]
    [property: SwaggerExample("2026-07-31")]
    DateOnly? ReconciledAt);

[SwaggerSchema("The headline numbers of the cash-flow projection — what the treasury looks at first.")]
public sealed record CashFlowKpis(
    [property: SwaggerSchema("Balance the projection starts from, in BRL — the sum across the accounts "
        + "in scope.")]
    [property: SwaggerExample("482350.00")]
    decimal StartingBalance,
    [property: SwaggerSchema("Lowest closing balance across the horizon, in BRL. Negative means the "
        + "projection runs out of money at some point.")]
    [property: SwaggerExample("-18400.00")]
    decimal LowestProjectedBalance,
    [property: SwaggerSchema("Which period that low point falls in.")]
    [property: SwaggerExample("Set/2026")]
    string LowestProjectedPeriodLabel,
    [property: SwaggerSchema("How many periods until the balance first goes negative. Null means it never "
        + "does within the horizon — not that the runway is zero.")]
    [property: SwaggerExample("3")]
    int? RunwayPeriods,
    [property: SwaggerSchema("Forecast inflows minus outflows over the horizon, in BRL.")]
    [property: SwaggerExample("-42000.00")]
    decimal NetResult,
    [property: SwaggerSchema("Total forecast inflows over the horizon, in BRL.")]
    [property: SwaggerExample("1840000.00")]
    decimal TotalInflowForecast,
    [property: SwaggerSchema("Total forecast outflows over the horizon, in BRL.")]
    [property: SwaggerExample("1882000.00")]
    decimal TotalOutflowForecast);

[SwaggerSchema("One bucket of the projection. A past period carries actuals alongside the forecast, so "
    + "the two can be compared; a future one carries forecast only.")]
public sealed record CashFlowPeriod(
    [property: SwaggerSchema("First day of the bucket.")]
    [property: SwaggerExample("2026-09-01")]
    DateOnly Start,
    [property: SwaggerSchema("Last day of the bucket, inclusive.")]
    [property: SwaggerExample("2026-09-30")]
    DateOnly End,
    [property: SwaggerSchema("Human label for the bucket, matching the requested granularity.")]
    [property: SwaggerExample("Set/2026")]
    string Label,
    [property: SwaggerSchema("Whether the bucket is already over. It is what tells a projection apart "
        + "from a record — only a past bucket has actuals.")]
    bool IsPast,
    [property: SwaggerSchema("Balance carried into the bucket, in BRL.")]
    [property: SwaggerExample("482350.00")]
    decimal OpeningBalance,
    [property: SwaggerSchema("Balance at the end of the bucket, in BRL — the next bucket's opening.")]
    [property: SwaggerExample("448200.00")]
    decimal ClosingBalance,
    [property: SwaggerSchema("Forecast money in, in BRL.")]
    [property: SwaggerExample("184000.00")]
    decimal InflowForecast,
    [property: SwaggerSchema("Forecast money out, in BRL.")]
    [property: SwaggerExample("218150.00")]
    decimal OutflowForecast,
    [property: SwaggerSchema("Forecast inflow minus outflow, in BRL.")]
    [property: SwaggerExample("-34150.00")]
    decimal NetForecast,
    [property: SwaggerSchema("Money that actually came in, in BRL. Null on a future bucket.")]
    decimal? InflowActual,
    [property: SwaggerSchema("Money that actually went out, in BRL. Null on a future bucket.")]
    decimal? OutflowActual,
    [property: SwaggerSchema("Whether the closing balance falls below the configured minimum reserve — "
        + "the warning threshold, which is not the same as going negative.")]
    bool BelowReserve);

[SwaggerSchema("The cash-flow projection: where the money is now and where the open titles say it is "
    + "going. A forecast built from scheduled installments — it is not a bank statement.")]
public sealed record CashFlowResponse(
    [property: SwaggerSchema("The date the projection was anchored at.")]
    [property: SwaggerExample("2026-08-19")]
    DateOnly AsOf,
    [property: SwaggerSchema("How the horizon is bucketed.")]
    CashFlowGranularity Granularity,
    [property: SwaggerSchema("Currency of every amount in the response.")]
    [property: SwaggerExample("BRL")]
    string Currency,
    [property: SwaggerSchema("Balance the projection starts from, in BRL.")]
    [property: SwaggerExample("482350.00")]
    decimal StartingBalance,
    [property: SwaggerSchema("The accounts that make up the starting balance.")]
    IReadOnlyList<CashFlowAccountBalance> Accounts,
    [property: SwaggerSchema("Headline numbers.")]
    CashFlowKpis Kpis,
    [property: SwaggerSchema("Receivables already past due, in BRL. Deliberately OUTSIDE the projection "
        + "— money that should have arrived and did not cannot be forecast into a future bucket.")]
    [property: SwaggerExample("96400.00")]
    decimal OverdueReceivable,
    [property: SwaggerSchema("The buckets, in chronological order.")]
    IReadOnlyList<CashFlowPeriod> Periods);

/// <summary>One installment composing a cash-flow period bucket (drill-down). Σ amount = bucket value.</summary>
[SwaggerSchema("One installment behind a cash-flow bucket, for the drill-down. Their amounts sum to the "
    + "bucket's value.")]
public sealed record CashFlowItemResponse(
    [property: SwaggerSchema("Which side this item is on: `payable` for money out, `receivable` for "
        + "money in.")]
    [property: SwaggerExample("payable")]
    string Kind, // "payable" | "receivable"
    [property: SwaggerSchema("The payable or receivable behind the installment — which one it is follows "
        + "Kind.")]
    Guid SourceId,
    [property: SwaggerSchema("The installment itself.")]
    Guid InstallmentId,
    [property: SwaggerSchema("Position within its title.")]
    [property: SwaggerExample("1")]
    int Sequence,
    [property: SwaggerSchema("Counterparty name — the vendor or the customer, following Kind.")]
    [property: SwaggerExample("Construtora Horizonte Ltda")]
    string CounterpartyName,
    [property: SwaggerSchema("Invoice number of the title, when one is registered.")]
    [property: SwaggerExample("00012345")]
    string? DocumentNumber,
    [property: SwaggerSchema("When the installment falls due — what places it in this bucket.")]
    [property: SwaggerExample("2026-09-10")]
    DateOnly DueDate,
    [property: SwaggerSchema("Amount contributed to the bucket, in BRL.")]
    [property: SwaggerExample("1430.25")]
    decimal Amount,
    [property: SwaggerSchema("Status of the installment.")]
    [property: SwaggerExample("Open")]
    string Status);
