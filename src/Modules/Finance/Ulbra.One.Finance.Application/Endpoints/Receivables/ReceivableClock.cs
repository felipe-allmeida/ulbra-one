namespace Ulbra.One.Finance.Application.Endpoints.Receivables;

/// <summary>Resolves "today" in Brazil local time for overdue calculations. Brazil has no DST since 2019,
/// so a fixed UTC-3 offset is correct and avoids OS time-zone database differences.</summary>
internal static class ReceivableClock
{
    private static readonly TimeSpan BrazilOffset = TimeSpan.FromHours(-3);

    public static DateOnly Today(TimeProvider timeProvider) =>
        DateOnly.FromDateTime(timeProvider.GetUtcNow().ToOffset(BrazilOffset).DateTime);
}
