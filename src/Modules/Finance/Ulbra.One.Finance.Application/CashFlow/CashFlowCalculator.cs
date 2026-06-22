using System.Globalization;

namespace Ulbra.One.Finance.Application.CashFlow;

/// <summary>Time bucket for the cash-flow projection.</summary>
public enum CashFlowGranularity { Day, Week, Month }

/// <summary>A single dated cash amount (forecast or actual). Amounts are positive magnitudes.</summary>
public readonly record struct DatedAmount(DateOnly Date, decimal Amount);

/// <summary>Inputs for a cash-flow projection. All amounts are positive magnitudes; direction is by collection.</summary>
public sealed record CashFlowInput(
    decimal StartingBalance,
    DateOnly AsOf,
    CashFlowGranularity Granularity,
    int Horizon,
    decimal MinReserve,
    IReadOnlyList<DatedAmount> ForecastInflows,
    IReadOnlyList<DatedAmount> ForecastOutflows,
    IReadOnlyList<DatedAmount> ActualInflows,
    IReadOnlyList<DatedAmount> ActualOutflows);

public sealed record CashFlowPeriodResult(
    DateOnly Start,
    DateOnly End,
    string Label,
    bool IsPast,
    decimal OpeningBalance,
    decimal ClosingBalance,
    decimal InflowForecast,
    decimal OutflowForecast,
    decimal NetForecast,
    decimal? InflowActual,
    decimal? OutflowActual,
    bool BelowReserve);

public sealed record CashFlowKpiResult(
    decimal StartingBalance,
    decimal LowestProjectedBalance,
    string LowestProjectedPeriodLabel,
    int? RunwayPeriods,
    decimal NetResult,
    decimal TotalInflowForecast,
    decimal TotalOutflowForecast);

public sealed record CashFlowResult(
    IReadOnlyList<CashFlowPeriodResult> Periods,
    CashFlowKpiResult Kpis,
    decimal OverdueReceivable);

/// <summary>
/// Pure cash-flow projection rule (no EF). Buckets dated forecast/actual amounts by granularity,
/// accumulates the balance from a starting anchor, and derives executive KPIs (break point, runway).
/// Rules: overdue payables (outflows dated before <c>AsOf</c>) are lumped into the current (first)
/// period; overdue receivables (inflows dated before <c>AsOf</c>) are EXCLUDED from the projection
/// and returned separately as <see cref="CashFlowResult.OverdueReceivable"/>.
/// </summary>
public static class CashFlowCalculator
{
    public static CashFlowResult Calculate(CashFlowInput input)
    {
        var horizon = Math.Max(1, input.Horizon);
        var asOf = input.AsOf;

        // Build the period windows from the period that contains AsOf, forward `horizon` periods.
        var periods = BuildPeriods(asOf, input.Granularity, horizon);
        var first = periods[0];
        var last = periods[^1];

        // Overdue receivables (inflows before AsOf) are excluded from the optimistic projection.
        var overdueReceivable = input.ForecastInflows
            .Where(x => x.Date < asOf)
            .Sum(x => x.Amount);

        // Bucket forecast amounts using the shared window rule (see CashFlowWindow): outflows fold
        // overdue into the current period; inflows exclude overdue. This is the same rule the drill
        // endpoint applies, so Σ drill items == period bucket value by construction.
        var inflowForecast = new decimal[horizon];
        var outflowForecast = new decimal[horizon];
        foreach (var item in input.ForecastInflows)
        {
            var idx = BucketOf(item.Date, CashFlowDirection.In, periods, asOf);
            if (idx >= 0) inflowForecast[idx] += item.Amount;
        }
        foreach (var item in input.ForecastOutflows)
        {
            var idx = BucketOf(item.Date, CashFlowDirection.Out, periods, asOf);
            if (idx >= 0) outflowForecast[idx] += item.Amount;
        }

        // Bucket actuals (only meaningful for past periods).
        var inflowActual = new decimal[horizon];
        var outflowActual = new decimal[horizon];
        foreach (var item in input.ActualInflows)
        {
            var idx = IndexOf(item.Date, periods);
            if (idx >= 0) inflowActual[idx] += item.Amount;
        }
        foreach (var item in input.ActualOutflows)
        {
            var idx = IndexOf(item.Date, periods);
            if (idx >= 0) outflowActual[idx] += item.Amount;
        }

        var results = new List<CashFlowPeriodResult>(horizon);
        var opening = input.StartingBalance;
        var lowest = decimal.MaxValue;
        var lowestLabel = first.Label;
        int? runway = null;
        decimal totalInflowForecast = 0;
        decimal totalOutflowForecast = 0;

        for (var i = 0; i < horizon; i++)
        {
            var window = periods[i];
            // A period "has past" once any of its days precede AsOf (true for the current, partially
            // elapsed period and any fully-elapsed one). Such periods expose actuals (previsto×realizado).
            var hasPast = window.Start < asOf;
            // Fully elapsed: the whole window is behind AsOf.
            var fullyPast = window.End < asOf;

            var inF = inflowForecast[i];
            var outF = outflowForecast[i];
            var net = inF - outF;
            totalInflowForecast += inF;
            totalOutflowForecast += outF;

            // Fully-past periods move the balance by what actually happened; the current and future
            // periods move by the (still-realizing) forecast. Forecast already excludes settled amounts
            // upstream, so the current period's forecast is the remaining expectation.
            var movement = fullyPast ? inflowActual[i] - outflowActual[i] : net;
            var closing = opening + movement;

            var belowReserve = closing < input.MinReserve;
            if (belowReserve && runway is null) runway = i;

            if (closing < lowest)
            {
                lowest = closing;
                lowestLabel = window.Label;
            }

            results.Add(new CashFlowPeriodResult(
                window.Start, window.End, window.Label, hasPast,
                opening, closing, inF, outF, net,
                hasPast ? inflowActual[i] : null,
                hasPast ? outflowActual[i] : null,
                belowReserve));

            opening = closing;
        }

        var kpis = new CashFlowKpiResult(
            input.StartingBalance,
            lowest == decimal.MaxValue ? input.StartingBalance : lowest,
            lowestLabel,
            runway,
            totalInflowForecast - totalOutflowForecast,
            totalInflowForecast,
            totalOutflowForecast);

        return new CashFlowResult(results, kpis, overdueReceivable);
    }

    private readonly record struct Window(DateOnly Start, DateOnly End, string Label);

    private static List<Window> BuildPeriods(DateOnly asOf, CashFlowGranularity granularity, int horizon)
    {
        var windows = new List<Window>(horizon);
        var cursor = PeriodStart(asOf, granularity);
        for (var i = 0; i < horizon; i++)
        {
            var (start, end, label) = PeriodOf(cursor, granularity);
            windows.Add(new Window(start, end, label));
            cursor = end.AddDays(1);
        }
        return windows;
    }

    /// <summary>
    /// Index of the bucket a dated forecast amount belongs to under the shared <see cref="CashFlowWindow"/>
    /// rule (overdue fold for outflows, overdue exclusion for inflows), or -1 if outside the horizon.
    /// </summary>
    private static int BucketOf(DateOnly date, CashFlowDirection direction, List<Window> periods, DateOnly today)
    {
        for (var i = 0; i < periods.Count; i++)
            if (CashFlowWindow.For(direction, periods[i].Start, periods[i].End, today).Contains(date, today))
                return i;
        return -1;
    }

    /// <summary>Index of the period containing <paramref name="date"/>, or -1 if outside the horizon.</summary>
    private static int IndexOf(DateOnly date, List<Window> periods)
    {
        for (var i = 0; i < periods.Count; i++)
            if (date >= periods[i].Start && date <= periods[i].End)
                return i;
        return -1;
    }

    private static DateOnly PeriodStart(DateOnly date, CashFlowGranularity granularity) => granularity switch
    {
        CashFlowGranularity.Day => date,
        CashFlowGranularity.Week => date.AddDays(-((((int)date.DayOfWeek + 6) % 7))), // Monday of the ISO week
        CashFlowGranularity.Month => new DateOnly(date.Year, date.Month, 1),
        _ => date,
    };

    private static (DateOnly Start, DateOnly End, string Label) PeriodOf(DateOnly start, CashFlowGranularity granularity)
    {
        switch (granularity)
        {
            case CashFlowGranularity.Day:
                return (start, start, start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            case CashFlowGranularity.Week:
            {
                var end = start.AddDays(6);
                var week = ISOWeek.GetWeekOfYear(start.ToDateTime(TimeOnly.MinValue));
                return (start, end, $"{ISOWeek.GetYear(start.ToDateTime(TimeOnly.MinValue))}-W{week:D2}");
            }
            case CashFlowGranularity.Month:
            {
                var end = start.AddMonths(1).AddDays(-1);
                return (start, end, start.ToString("yyyy-MM", CultureInfo.InvariantCulture));
            }
            default:
                return (start, start, start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }
    }
}
