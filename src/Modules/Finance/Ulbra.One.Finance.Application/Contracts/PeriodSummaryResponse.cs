namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>Count and summed amount of bank transactions in one <see cref="Domain.MatchStatus"/> bucket.</summary>
public sealed record StatusBucket(int Count, decimal Total);

/// <summary>
/// Aggregated view of the bank transactions in a period, grouped by reconciliation status — the
/// "pagamentos/cobranças do período" summary (conciliado × pendente × ignorado), plus debit/credit totals.
/// </summary>
public sealed record PeriodSummaryResponse(
    DateOnly? From,
    DateOnly? To,
    StatusBucket Reconciled,
    StatusBucket Suggested,
    StatusBucket Unmatched,
    StatusBucket Ignored,
    decimal DebitTotal,
    decimal CreditTotal);
