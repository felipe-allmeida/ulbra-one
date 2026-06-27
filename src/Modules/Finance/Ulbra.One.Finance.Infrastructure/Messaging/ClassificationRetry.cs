namespace Ulbra.One.Finance.Infrastructure.Messaging;

/// <summary>Escalating redelivery schedule for payable auto-classification: ~3 fast tries then growing
/// gaps, 10 total attempts. After the last, the consumer settles the payable into PendingClassification.</summary>
public static class ClassificationRetry
{
    /// <summary>9 redelivery intervals (1 initial delivery + 9 redeliveries = 10 total attempts).</summary>
    public static readonly TimeSpan[] Intervals =
    [
        TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(1),   // fast
        TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(20), TimeSpan.FromHours(1),
        TimeSpan.FromHours(4), TimeSpan.FromHours(12), TimeSpan.FromHours(24),         // escalating
    ];

    /// <summary>Settle (give up retrying) once the redelivery count reaches this (no interval left).</summary>
    public const int MaxRedeliveries = 9;
}
