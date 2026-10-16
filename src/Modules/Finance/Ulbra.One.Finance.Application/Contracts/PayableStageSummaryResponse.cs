using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>Count of titles and the sum of their total amounts for one workflow stage.
/// <see cref="NetTotalAmount"/> (sum net of withholdings) is filled only where the net is locked and
/// meaningful — today just the payables Payment bucket (Scheduled); null elsewhere.</summary>
[SwaggerSchema("How much work is sitting in one stage, and what it is worth.")]
public sealed record StageBucket(
    [property: SwaggerSchema("How many titles are in the stage.")]
    [property: SwaggerExample("42")]
    int Count,
    [property: SwaggerSchema("Sum of their document totals, in BRL — before withholdings.")]
    [property: SwaggerExample("184320.00")]
    decimal TotalAmount,
    [property: SwaggerSchema("Sum net of withholdings, in BRL. Filled only where the net is already "
        + "locked and therefore meaningful — today just the payments bucket. Null elsewhere means not "
        + "applicable, not zero.")]
    [property: SwaggerExample("176420.75")]
    decimal? NetTotalAmount = null);

/// <summary>Pending-work summary per payable workflow stage, keyed by the UX stage names.</summary>
[SwaggerSchema("Pending work across the payable workflow, one bucket per stage — what the tab badges "
    + "count. FiscalDocumentPending overlaps the others: it is a condition, not a stage of its own.")]
public sealed record PayableStageSummaryResponse(
    [property: SwaggerSchema("Titles waiting to be completed after entry.")]
    StageBucket Entry,
    [property: SwaggerSchema("Titles waiting for an accounting classification.")]
    StageBucket Classification,
    [property: SwaggerSchema("Titles waiting for approval.")]
    StageBucket Approval,
    [property: SwaggerSchema("Titles waiting for the accounting review.")]
    StageBucket Accounting,
    [property: SwaggerSchema("Titles in análise tributária — the Tributário must validate the retenções before "
        + "the title can be paid or posted.")]
    StageBucket TaxAnalysis,
    [property: SwaggerSchema("Titles scheduled and waiting to be paid — the treasury queue.")]
    StageBucket Payment,
    [property: SwaggerSchema("Titles already settled and reconciled.")]
    StageBucket Reconciled,
    [property: SwaggerSchema("Titles scheduled without a Nota Fiscal. Counted in their own stage too, "
        + "so this bucket must not be added to the others.")]
    StageBucket FiscalDocumentPending,
    [property: SwaggerSchema("The subset of FiscalDocumentPending still sitting in the accounting stage - "
        + "the titles the Contabil can attach a Nota Fiscal to right now. Use this for the Pendencias "
        + "\"Nota Fiscal\" tab: it is exactly what that tab lists, while the wider bucket also spans tax "
        + "analysis, payment and reconciled titles whose NF is chased elsewhere.")]
    StageBucket FiscalDocumentPendingAccounting);
