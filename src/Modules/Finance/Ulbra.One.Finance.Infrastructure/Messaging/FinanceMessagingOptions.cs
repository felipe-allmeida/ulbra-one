namespace Ulbra.One.Finance.Infrastructure.Messaging;

/// <summary>Options for the Finance async messaging pipeline. Bound from "Finance:Messaging" in FinanceModule.</summary>
public sealed class FinanceMessagingOptions
{
    /// <summary>When on (default), a Purchase-origin payable auto-advances to Scheduled in
    /// <see cref="PayableReceivedConsumer"/> (skips the Finance gates); off = the old classification esteira.</summary>
    public bool AutoScheduleRequisitionPayables { get; init; } = true;
}
