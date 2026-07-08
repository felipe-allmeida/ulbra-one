namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>Count of titles and the sum of their total amounts for one workflow stage.</summary>
public sealed record StageBucket(int Count, decimal TotalAmount);

/// <summary>Pending-work summary per payable workflow stage, keyed by the UX stage names.</summary>
public sealed record PayableStageSummaryResponse(
    StageBucket Entry,
    StageBucket Classification,
    StageBucket Approval,
    StageBucket Accounting,
    StageBucket Payment,
    StageBucket Reconciled);
