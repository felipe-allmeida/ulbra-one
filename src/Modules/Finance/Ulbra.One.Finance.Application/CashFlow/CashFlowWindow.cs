namespace Ulbra.One.Finance.Application.CashFlow;

/// <summary>Direction of a cash-flow movement.</summary>
public enum CashFlowDirection { In, Out }

/// <summary>
/// The single source of truth for "which dated items belong to a cash-flow bucket".
/// Shared by the pure <see cref="CashFlowCalculator"/> (forecast bucketing) and the drill-down
/// endpoint so the invariant "Σ drill items == period bucket value" is guaranteed by code, not
/// only by an integration test.
/// </summary>
/// <remarks>
/// Rules for a bucket window <c>[from, to]</c> evaluated against <paramref name="today"/> (the AsOf):
/// <list type="bullet">
/// <item><b>Outflows</b> fold overdue (date &lt; today) into the current period — the period that
/// contains <paramref name="today"/>. Such a period therefore also includes anything due before its
/// own start. Non-current periods only contain items due within <c>[from, to]</c>.</item>
/// <item><b>Inflows</b> never include overdue: the current period's lower bound clamps up to
/// <paramref name="today"/>, so receivables due before today are excluded from the projection.</item>
/// </list>
/// An item with date <c>d</c> belongs to the window when:
/// <c>d &lt;= Upper &amp;&amp; (d &gt;= Lower || (IncludeOverdue &amp;&amp; d &lt; today))</c>.
/// </remarks>
public readonly record struct CashFlowWindow(DateOnly Lower, DateOnly Upper, bool IncludeOverdue)
{
    public static CashFlowWindow For(CashFlowDirection direction, DateOnly from, DateOnly to, DateOnly today)
    {
        var isCurrent = from <= today && today <= to;
        return direction == CashFlowDirection.Out
            // Outflows: the current period absorbs overdue; others keep their natural lower bound.
            ? new CashFlowWindow(from, to, isCurrent)
            // Inflows: clamp the lower bound up to today so overdue receivables are never listed.
            : new CashFlowWindow(from < today ? today : from, to, IncludeOverdue: false);
    }

    /// <summary>Whether a dated item falls inside this window.</summary>
    public bool Contains(DateOnly date, DateOnly today) =>
        date <= Upper && (date >= Lower || (IncludeOverdue && date < today));
}
