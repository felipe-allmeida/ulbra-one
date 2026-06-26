namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>Pending-work summary per receivable workflow stage. Entry/Receipt split the AwaitingReceipt status.</summary>
public sealed record ReceivableStageSummaryResponse(
    StageBucket Entry,
    StageBucket Receipt,
    StageBucket Reconciled);
