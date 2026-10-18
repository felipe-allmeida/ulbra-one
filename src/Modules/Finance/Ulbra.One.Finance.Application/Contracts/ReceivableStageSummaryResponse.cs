using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>Pending-work summary per receivable workflow stage. Entry/Receipt split the AwaitingReceipt status.</summary>
[SwaggerSchema("Pending work across the receivable workflow. Entry and Receipt are two halves of the "
    + "same AwaitingReceipt status, split for the UI rather than by the domain.")]
public sealed record ReceivableStageSummaryResponse(
    [property: SwaggerSchema("Titles waiting to be completed after entry.")]
    StageBucket Entry,
    [property: SwaggerSchema("Titles waiting for the money to arrive.")]
    StageBucket Receipt,
    [property: SwaggerSchema("Titles already received and reconciled.")]
    StageBucket Reconciled);
