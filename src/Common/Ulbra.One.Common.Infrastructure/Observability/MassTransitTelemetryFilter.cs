namespace Ulbra.One.Common.Infrastructure.Observability;

/// <summary>
/// Identifies the MassTransit outbox/inbox polling sweeps, which are dropped from EF telemetry —
/// they were 99% of all database spans (#228).
/// </summary>
public static class MassTransitTelemetryFilter
{
    private const string Schema = "masstransit";

    // outbox_message is deliberately absent: those rows are business events on their way to the
    // broker, and their spans are how you tell whether an event actually left.
    private static readonly string[] SweepTables = ["inbox_state", "outbox_state"];

    public static bool IsOutboxPolling(string? commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return false;
        }

        if (!commandText.Contains(Schema, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var table in SweepTables)
        {
            if (commandText.Contains(table, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
