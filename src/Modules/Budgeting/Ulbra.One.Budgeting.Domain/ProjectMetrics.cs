namespace Ulbra.One.Budgeting.Domain;

/// <summary>
/// Accumulated (not monthly) spend tracking for a <see cref="Entities.Project"/>.
/// <c>Previsto</c> is what ALL linked (non-cancelled) requisitions would cost — a projection of the full
/// pipeline; <c>Realizado</c> what was actually paid; both include the project's pre-system consumption.
/// <c>Saldo = BudgetLimit − real consumption</c>, where "real consumption" counts only requisitions that
/// reached payment (InPayment+) — the pipeline in triage/approval does NOT reduce the balance (AEL-844). A
/// negative balance means the ceiling is really blown (<see cref="IsOverCeiling"/>).
/// </summary>
public readonly record struct ProjectMetrics(
    decimal BudgetLimit,
    decimal Previsto,
    decimal Realizado,
    decimal Saldo)
{
    /// <summary>True when the committed total already exceeds the ceiling.</summary>
    public bool IsOverCeiling => Saldo < 0m;

    public decimal PercentPrevisto => PercentOfCeiling(Previsto);
    public decimal PercentRealizado => PercentOfCeiling(Realizado);

    /// <summary>Share of the ceiling, in percent. A zero ceiling yields 0 rather than dividing by zero.</summary>
    private decimal PercentOfCeiling(decimal value) =>
        BudgetLimit > 0m ? decimal.Round(value / BudgetLimit * 100m, 2, MidpointRounding.AwayFromZero) : 0m;
}
