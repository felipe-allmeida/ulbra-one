using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>One due-date group of the payment queue (count + net total across ALL pages).</summary>
[SwaggerSchema("One due-date group of the payment queue, counted across ALL pages — not just the "
    + "visible one.")]
public sealed record PaymentQueueDayGroup(
    [property: SwaggerSchema("The due date this group covers.")]
    [property: SwaggerExample("2026-08-10")]
    DateOnly DueDate,
    [property: SwaggerSchema("Installments falling due that day.")]
    [property: SwaggerExample("18")]
    int Count,
    [property: SwaggerSchema("What they would actually pay, in BRL — net of withholdings.")]
    [property: SwaggerExample("24180.50")]
    decimal NetTotal);

/// <summary>
/// Whole-set aggregates for the payment queue's group headers ("Vencidos", "Vence hoje", per-day),
/// honoring the same filters as the list so headers always agree with the visible rows.
/// Amounts are net of withholdings — what a remittance would actually pay.
/// </summary>
[SwaggerSchema("Whole-set totals for the payment queue's group headers. It honours the same filters as "
    + "the list but spans every page, so a header never disagrees with what the operator is filtering. "
    + "Every amount is net of withholdings — what a remittance would really pay.")]
public sealed record PaymentQueueSummaryResponse(
    [property: SwaggerSchema("The date the queue is anchored at — what counts as today.")]
    [property: SwaggerExample("2026-08-19")]
    DateOnly AsOf,
    [property: SwaggerSchema("Installments already past due.")]
    [property: SwaggerExample("7")]
    int OverdueCount,
    [property: SwaggerSchema("What the overdue installments would pay, in BRL.")]
    [property: SwaggerExample("9840.00")]
    decimal OverdueNetTotal,
    /// <summary>Per-due-date groups from <see cref="AsOf"/> forward (the AsOf entry is "Vence hoje").</summary>
    [property: SwaggerSchema("One group per due date from AsOf forward. The AsOf group is \"vence "
        + "hoje\"; anything earlier is folded into the overdue counters above rather than appearing here.")]
    IReadOnlyList<PaymentQueueDayGroup> Groups,
    [property: SwaggerSchema("Installments in the queue in total, overdue included.")]
    [property: SwaggerExample("184")]
    int TotalCount,
    [property: SwaggerSchema("What the whole queue would pay, in BRL.")]
    [property: SwaggerExample("248390.75")]
    decimal TotalNetTotal);
