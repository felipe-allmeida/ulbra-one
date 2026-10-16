using Ulbra.One.Budgeting.Domain.Entities;

namespace Ulbra.One.Budgeting.Application.BudgetControl;

/// <summary>Per-competency budget ledger for a (cost center, unit): the ceiling (<see cref="Orcado"/>) and its
/// consumption split into realizado (paid), empenhado (committed but not yet liquidated) and previsto (queued).</summary>
public sealed record BudgetMonth(string Competency, decimal Orcado, decimal Realized, decimal Committed, decimal Forecast)
{
    /// <summary>Consumido = realizado + empenhado.</summary>
    public decimal Consumed => Realized + Committed;

    /// <summary>REAL available balance = budgeted − consumed. Only what reached payment (InPayment+) reduces the
    /// available; the forecast (pipeline in triage/complement/approval) does NOT reduce it — it becomes the
    /// "available after approval" projection on screens (AEL-844).</summary>
    public decimal Available => Orcado - Consumed;

    /// <summary>Whether a ceiling exists for this month (0 = no cell). No cell → the evaluation BLOCKS
    /// (cost center with no budget; AEL-796), see <see cref="BudgetControlService"/>.</summary>
    public bool Budgeted => Orcado > 0m;
}

/// <summary>A competency where committing the candidate would exceed the available budget.</summary>
public sealed record BudgetBreach(string Competency, decimal Orcado, decimal Available, decimal Cost);

/// <summary>Budget ledger for a single cost center (natureza) in one competency — one row of the
/// "orçamento por natureza" summary. Same math as <see cref="BudgetMonth"/>.</summary>
public sealed record CostCenterBudget(
    Guid CostCenterId, string CostCenterName, decimal Orcado, decimal Realized, decimal Committed, decimal Forecast)
{
    public decimal Consumed => Realized + Committed;
    /// <summary>REAL available = budgeted − consumed (forecast/pipeline does not reduce it — projection only on screens).</summary>
    public decimal Available => Orcado - Consumed;
    public bool Budgeted => Orcado > 0m;
}

/// <summary>Total realized (non-reversed payments) of a cost center, summed over ALL competencies —
/// feeds the "spend × budget" card on the cost-center list.</summary>
public sealed record CostCenterRealized(Guid CostCenterId, decimal Realized);

/// <summary>Aggregate budget ledger for a whole company/unit in one competency: the ceiling summed over all
/// cost centers, and the consumption of ALL requisitions split into consumido (paid+committed) and previsto
/// (the queue in triage/approval/complement). Feeds the queue-projection sparkline.</summary>
public sealed record AggregateBudgetMonth(string Competency, decimal Orcado, decimal Consumed, decimal Forecast)
{
    public bool Budgeted => Orcado > 0m;
}

/// <summary>Multi-month projection of a company/unit's whole queue against the budget: the aggregate monthly
/// ledger, the competencies the queue would breach (consumido + previsto &gt; orçado), and the total queued value.</summary>
public sealed record QueueProjection(
    IReadOnlyList<AggregateBudgetMonth> Months, IReadOnlyList<string> Breaches, decimal QueueTotal);

/// <summary>Result of evaluating a candidate requisition against the budget: the per-month ledger over the
/// candidate's window, the months it breaches, the months it touches with no ceiling (unbudgeted — which
/// also block, AEL-796), and whether any breach occurred.</summary>
public sealed record BudgetEvaluation(
    IReadOnlyList<BudgetMonth> Months,
    IReadOnlyList<BudgetBreach> Breaches,
    IReadOnlyList<string> Unbudgeted,
    bool CandidateExceeds);

/// <summary>Authoritative, server-side budget control (successor to the interim client-side sum). Computes
/// orçado × consumido × previsto per competency from the Core budget cells + the requisitions of the
/// dimension, and decides whether a candidate requisition would breach any month's ceiling.</summary>
public interface IBudgetControl
{
    /// <summary>Per-month ledger for a dimension window, excluding one requisition (used by the panel; the
    /// create form overlays its own unsaved value on top). Empty window returns no months.</summary>
    Task<IReadOnlyList<BudgetMonth>> DescribeAsync(
        Guid costCenterId, Guid unitId, string from, string to, Guid? excludeRequisitionId, CancellationToken ct);

    /// <summary>Evaluates whether committing <paramref name="candidate"/> would breach its budget. Aggregates
    /// over the OTHER requisitions of the dimension (excludes the candidate itself). Months with no ceiling are
    /// reported as unbudgeted and also block (cost center with no budget; AEL-796).</summary>
    Task<BudgetEvaluation> EvaluateCandidateAsync(Requisition candidate, CancellationToken ct);

    /// <summary>Budget summary by cost center (natureza) for a company in a single competency — one row per
    /// cost center that has a ceiling or activity. <paramref name="unitId"/> is optional: null consolidates
    /// across the company's units; a value scopes to that unit.</summary>
    Task<IReadOnlyList<CostCenterBudget>> SummarizeByCostCenterAsync(
        Guid companyId, Guid? unitId, string competency, CancellationToken ct);

    /// <summary>Total realized per cost center (sum of the company's non-reversed requisition payments, over
    /// all competencies). One row per cost center with non-zero realized.</summary>
    Task<IReadOnlyList<CostCenterRealized>> SummarizeRealizedByCostCenterAsync(Guid companyId, CancellationToken ct);

    /// <summary>Multi-month projection of the WHOLE queue (all cost centers) for a company/unit over the
    /// inclusive range <c>[from, to]</c>: ceiling summed across cost centers vs consumido + previsto (fila) per
    /// competency, marking the months the queue would breach. <paramref name="unitId"/> null = whole company.</summary>
    Task<QueueProjection> SummarizeQueueProjectionAsync(
        Guid companyId, Guid? unitId, string from, string to, CancellationToken ct);
}
