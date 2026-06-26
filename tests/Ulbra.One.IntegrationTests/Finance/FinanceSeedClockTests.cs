namespace Ulbra.One.IntegrationTests.Finance;

public class FinanceSeedClockTests
{
    [Fact]
    public void Today_matches_the_server_brazil_business_clock_formula()
    {
        // Mirrors ReceivableClock.Today (UTC-3). If these diverge, receipt-dated-today seeds can be
        // rejected as future-dated near UTC midnight.
        var expected = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-3)).DateTime);
        Assert.Equal(expected, FinanceSeed.Today());
    }
}
