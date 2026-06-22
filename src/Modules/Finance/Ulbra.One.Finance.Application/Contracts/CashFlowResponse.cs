using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Finance.Application.CashFlow;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>Current balance of a bank account in scope, with its reconciliation anchor (if any).</summary>
public sealed record CashFlowAccountBalance(
    BankAccountId BankAccountId, string Alias, decimal CurrentBalance, DateOnly? ReconciledAt);

public sealed record CashFlowKpis(
    decimal StartingBalance,
    decimal LowestProjectedBalance,
    string LowestProjectedPeriodLabel,
    int? RunwayPeriods,
    decimal NetResult,
    decimal TotalInflowForecast,
    decimal TotalOutflowForecast);

public sealed record CashFlowPeriod(
    DateOnly Start,
    DateOnly End,
    string Label,
    bool IsPast,
    decimal OpeningBalance,
    decimal ClosingBalance,
    decimal InflowForecast,
    decimal OutflowForecast,
    decimal NetForecast,
    decimal? InflowActual,
    decimal? OutflowActual,
    bool BelowReserve);

public sealed record CashFlowResponse(
    DateOnly AsOf,
    CashFlowGranularity Granularity,
    string Currency,
    decimal StartingBalance,
    IReadOnlyList<CashFlowAccountBalance> Accounts,
    CashFlowKpis Kpis,
    decimal OverdueReceivable,
    IReadOnlyList<CashFlowPeriod> Periods);

/// <summary>One installment composing a cash-flow period bucket (drill-down). Σ amount = bucket value.</summary>
public sealed record CashFlowItemResponse(
    string Kind, // "payable" | "receivable"
    Guid SourceId,
    Guid InstallmentId,
    int Sequence,
    string CounterpartyName,
    string? DocumentNumber,
    DateOnly DueDate,
    decimal Amount,
    string Status);
