namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>Count of titles and the sum of their total amounts for one workflow stage.
/// <see cref="NetTotalAmount"/> (sum net of withholdings) is filled only where the net is locked and
/// meaningful — today just the payables Payment bucket (Scheduled); null elsewhere.</summary>
public sealed record StageBucket(int Count, decimal TotalAmount, decimal? NetTotalAmount = null);

/// <summary>Pending-work summary per payable workflow stage, keyed by the UX stage names.</summary>
public sealed record PayableStageSummaryResponse(
    StageBucket Entry,
    StageBucket Classification,
    StageBucket Approval,
    StageBucket Accounting,
    StageBucket Payment,
    StageBucket Reconciled,
    StageBucket FiscalDocumentPending);
