using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>Count and summed amount of bank transactions in one <see cref="Domain.MatchStatus"/> bucket.</summary>
[SwaggerSchema("How many statement lines sit in one reconciliation status, and what they add up to.")]
public sealed record StatusBucket(
    [property: SwaggerSchema("Lines in this status.")]
    [property: SwaggerExample("162")]
    int Count,
    [property: SwaggerSchema("Sum of their amounts, in BRL.")]
    [property: SwaggerExample("248390.75")]
    decimal Total);

/// <summary>
/// Aggregated view of the bank transactions in a period, grouped by reconciliation status — the
/// "pagamentos/cobranças do período" summary (conciliado × pendente × ignorado), plus debit/credit totals.
/// </summary>
[SwaggerSchema("Bank statement lines in a period, grouped by how far reconciliation has got. The four "
    + "status buckets are mutually exclusive and cover every line; the debit and credit totals cut the "
    + "same lines a different way, so the two groupings must not be added together.")]
public sealed record PeriodSummaryResponse(
    [property: SwaggerSchema("Start of the period. Null means from the earliest imported line.")]
    [property: SwaggerExample("2026-08-01")]
    DateOnly? From,
    [property: SwaggerSchema("End of the period, inclusive.")]
    [property: SwaggerExample("2026-08-31")]
    DateOnly? To,
    [property: SwaggerSchema("Lines already reconciled to a payment.")]
    StatusBucket Reconciled,
    [property: SwaggerSchema("Lines with a suggested match awaiting confirmation.")]
    StatusBucket Suggested,
    [property: SwaggerSchema("Lines with nothing to match them to — the real backlog.")]
    StatusBucket Unmatched,
    [property: SwaggerSchema("Lines deliberately set aside, so they stop showing as pending without "
        + "being reconciled to anything.")]
    StatusBucket Ignored,
    [property: SwaggerSchema("Total that left the account in the period, in BRL.")]
    [property: SwaggerExample("248390.75")]
    decimal DebitTotal,
    [property: SwaggerSchema("Total that arrived in the period, in BRL.")]
    [property: SwaggerExample("312400.00")]
    decimal CreditTotal);
