using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Budgeting.Application.BudgetControl;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Budgeting.Application.Contracts;

/// <summary>Per-competency budget ledger row for the requisition panel. <paramref name="Cost"/> is the
/// candidate requisition's exact contribution to this competency (rateio resto-na-última) quando avaliada;
/// 0 no painel de dimensão sem candidato. Evita a aproximação uniforme no cliente (AEL-844).</summary>
[SwaggerSchema("One month of the budget ledger for the requisition panel. The figures overlap — "
    + "Consumed is Realized plus Committed — so they are stages of one amount, not parts to be summed.")]
public sealed record BudgetMonthResponse(
    [property: SwaggerSchema("The month (`YYYY-MM`).")]
    [property: SwaggerExample("2026-08")]
    string Competency,
    [property: SwaggerSchema("The month's ceiling, in BRL. Zero when unbudgeted — read Budgeted to tell "
        + "that apart from a ceiling deliberately set to zero.")]
    [property: SwaggerExample("40000.00")]
    decimal Orcado,
    [property: SwaggerSchema("Paid in the month, in BRL.")]
    [property: SwaggerExample("18200.00")]
    decimal Realized,
    [property: SwaggerSchema("Empenhado, in BRL — approved but not yet paid.")]
    [property: SwaggerExample("9400.00")]
    decimal Committed,
    [property: SwaggerSchema("Previsto, in BRL — in the pipeline but not yet approved, so it does not "
        + "consume the ceiling.")]
    [property: SwaggerExample("5000.00")]
    decimal Forecast,
    [property: SwaggerSchema("What consumes the ceiling, in BRL: Realized plus Committed.")]
    [property: SwaggerExample("27600.00")]
    decimal Consumed,
    [property: SwaggerSchema("Ceiling minus Consumed, in BRL — what a new requisition is checked against.")]
    [property: SwaggerExample("12400.00")]
    decimal Available,
    [property: SwaggerSchema("Whether a budget cell exists for the month.")]
    bool Budgeted,
    [property: SwaggerSchema("What the candidate requisition would add to THIS month, in BRL, split "
        + "exactly (remainder on the last month). Zero when no candidate is being evaluated — the panel "
        + "shows the ledger alone. It exists so the client never has to approximate the split itself.")]
    [property: SwaggerExample("4166.67")]
    decimal Cost = 0m)
{
    public static BudgetMonthResponse From(BudgetMonth m, decimal cost = 0m) =>
        new(m.Competency, m.Orcado, m.Realized, m.Committed, m.Forecast, m.Consumed, m.Available, m.Budgeted, cost);
}

/// <summary>A competency the candidate breaches (cost above available).</summary>
[SwaggerSchema("A month the candidate would breach — its cost exceeds what is available. Informational: "
    + "it warns the approver, it does not block the approval.")]
public sealed record BudgetBreachResponse(
    [property: SwaggerSchema("The month that breaches (`YYYY-MM`).")]
    [property: SwaggerExample("2026-09")]
    string Competency,
    [property: SwaggerSchema("The month's ceiling, in BRL.")]
    [property: SwaggerExample("40000.00")]
    decimal Orcado,
    [property: SwaggerSchema("What was still available before the candidate, in BRL.")]
    [property: SwaggerExample("3000.00")]
    decimal Available,
    [property: SwaggerSchema("What the candidate would add to this month, in BRL.")]
    [property: SwaggerExample("4166.67")]
    decimal Cost)
{
    public static BudgetBreachResponse From(BudgetBreach b) => new(b.Competency, b.Orcado, b.Available, b.Cost);
}

/// <summary>Aggregate monthly ledger row (company/unit, all cost centers) for the queue-projection sparkline.</summary>
[SwaggerSchema("One month of the aggregate ledger across every cost center of a company or unit — the "
    + "sparkline behind the queue projection.")]
public sealed record AggregateBudgetMonthResponse(
    [property: SwaggerSchema("The month (`YYYY-MM`).")]
    [property: SwaggerExample("2026-08")]
    string Competency,
    [property: SwaggerSchema("Sum of the ceilings, in BRL.")]
    [property: SwaggerExample("480000.00")]
    decimal Orcado,
    [property: SwaggerSchema("Sum of what consumes those ceilings, in BRL.")]
    [property: SwaggerExample("312000.00")]
    decimal Consumed,
    [property: SwaggerSchema("Sum of what is in the pipeline, in BRL.")]
    [property: SwaggerExample("64000.00")]
    decimal Forecast,
    [property: SwaggerSchema("Whether every cost center in the aggregate has a cell for the month. False "
        + "means at least one is unbudgeted, so Orcado understates the real ceiling.")]
    bool Budgeted)
{
    public static AggregateBudgetMonthResponse From(AggregateBudgetMonth m) =>
        new(m.Competency, m.Orcado, m.Consumed, m.Forecast, m.Budgeted);
}

/// <summary>Server-computed projection of a company/unit's whole queue against the budget over a horizon: the
/// aggregate monthly ledger, the competencies the queue would breach, and the total queued value.</summary>
[SwaggerSchema("What the whole pending queue would do to the budget over a horizon. Computed on the "
    + "server so every client shows the same projection.")]
public sealed record QueueProjectionResponse(
    [property: SwaggerSchema("The aggregate ledger, month by month over the horizon.")]
    IReadOnlyList<AggregateBudgetMonthResponse> Months,
    [property: SwaggerSchema("Competencies (`YYYY-MM`) the queue would breach if everything pending were "
        + "approved.")]
    IReadOnlyList<string> Breaches,
    [property: SwaggerSchema("Total value sitting in the queue, in BRL.")]
    [property: SwaggerExample("184000.00")]
    decimal QueueTotal)
{
    public static QueueProjectionResponse From(QueueProjection p) =>
        new(p.Months.Select(AggregateBudgetMonthResponse.From).ToList(), p.Breaches, p.QueueTotal);
}

/// <summary>Server-computed budget panel for a (cost center, unit) window: the monthly ledger plus, when a
/// candidate requisition is evaluated, the months it breaches, the months it touches with no ceiling, and
/// whether it would exceed the budget.</summary>
[SwaggerSchema("The budget panel for a (cost center, unit) window. Without a candidate it is just the "
    + "ledger; with one, it also says which months it would breach.")]
public sealed record BudgetControlResponse(
    [property: SwaggerSchema("The monthly ledger over the window.")]
    IReadOnlyList<BudgetMonthResponse> Months,
    [property: SwaggerSchema("Months the candidate would breach. Empty when no candidate is being "
        + "evaluated.")]
    IReadOnlyList<BudgetBreachResponse> Breaches,
    [property: SwaggerSchema("Competencies (`YYYY-MM`) the candidate touches that have no ceiling at all. "
        + "Distinct from a breach — there is nothing to exceed, which is its own problem.")]
    IReadOnlyList<string> Unbudgeted,
    [property: SwaggerSchema("Whether the candidate would exceed the budget in any month. A warning for "
        + "the approver, not a block.")]
    bool CandidateExceeds)
{
    public static BudgetControlResponse FromMonths(IReadOnlyList<BudgetMonth> months) =>
        new(months.Select(m => BudgetMonthResponse.From(m)).ToList(), [], [], false);

    /// <summary><paramref name="costOf"/> devolve o custo exato do candidato por competência (resto-na-última),
    /// carregado em cada mês para o cliente exibir "Este pedido" sem aproximar (AEL-844).</summary>
    public static BudgetControlResponse From(BudgetEvaluation e, Func<string, decimal>? costOf = null) =>
        new(e.Months.Select(m => BudgetMonthResponse.From(m, costOf?.Invoke(m.Competency) ?? 0m)).ToList(),
            e.Breaches.Select(BudgetBreachResponse.From).ToList(),
            e.Unbudgeted, e.CandidateExceeds);
}

/// <summary>Batch of <see cref="BudgetControlResponse"/> keyed by requisition id — one call evaluates a whole
/// approval/triage queue instead of one request per requisition.</summary>
[SwaggerSchema("Budget-control panels keyed by requisition id. Requisitions that do not exist are omitted.")]
public sealed record RequisitionBudgetControlsResponse(
    [property: SwaggerSchema("The budget-control panel per requisition, keyed by requisition id.")]
    IReadOnlyDictionary<string, BudgetControlResponse> Controls);
