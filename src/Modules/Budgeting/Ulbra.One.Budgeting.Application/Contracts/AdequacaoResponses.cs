using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Budgeting.Application.Contracts;

/// <summary>Response shape for an adequação status transition (who/when/from→to). Enums serialize as strings.</summary>
[SwaggerSchema("One step of the adequação's history — who moved it, from where to where, and why.")]
public sealed record BudgetAdjustmentTransitionResponse(
    [property: SwaggerSchema("What was done.")]
    BudgetAdjustmentAction Action,
    [property: SwaggerSchema("Status it was in before.")]
    BudgetAdjustmentStatus FromStatus,
    [property: SwaggerSchema("Status it moved to.")]
    BudgetAdjustmentStatus ToStatus,
    [property: SwaggerSchema("Who performed the action.")]
    UserId ActorId,
    [property: SwaggerSchema("Display name of the actor. Only the single-adequação endpoint resolves it; "
        + "the list endpoints leave it null rather than pay a directory lookup per row.")]
    [property: SwaggerExample("Maria Silva")]
    string? ActorName,
    [property: SwaggerSchema("The actor's Google profile photo, resolved under the same rule as ActorName. "
        + "Null for an unresolved actor and for a user with no photo — render initials instead.")]
    [property: SwaggerExample("https://lh3.googleusercontent.com/a/ACg8ocK")]
    string? ActorPictureUrl,
    [property: SwaggerSchema("When it happened.")]
    DateTimeOffset OccurredAt,
    [property: SwaggerSchema("Justification given.")]
    string? Reason)
{
    public static BudgetAdjustmentTransitionResponse From(
        BudgetAdjustmentTransition t, IReadOnlyDictionary<Guid, UserProfile>? actors = null)
    {
        var profile = actors?.GetValueOrDefault(t.ActorId);
        return new(t.Action, t.FromStatus, t.ToStatus, new UserId(t.ActorId),
            profile?.Name, profile?.PictureUrl, t.OccurredAt, t.Reason);
    }
}

/// <summary>Response shape for an <see cref="BudgetAdjustment"/> (budget adjustment). Enums serialize as
/// strings (global JsonStringEnumConverter). <see cref="StatusChangedAt"/> feeds the stage SLA and is
/// available in lists too.</summary>
[SwaggerSchema("A budget adjustment (adequação) — a request to change a monthly ceiling. Nothing moves "
    + "in the budget until it is approved; while it is pending it is only an intention.")]
public sealed record BudgetAdjustmentResponse(
    BudgetAdjustmentId Id,
    [property: SwaggerSchema("Per-company sequential code — what people cite instead of the id.")]
    [property: SwaggerExample("57")]
    int Code,
    [property: SwaggerSchema("Company the adjustment belongs to. Derived from the unit.")]
    CompanyId CompanyId,
    [property: SwaggerSchema("Unit the adjustment belongs to.")]
    UnitId UnitId,
    [property: SwaggerSchema("Cost center whose ceiling changes.")]
    CostCenterId CostCenterId,
    [property: SwaggerSchema("What the adjustment does: Supplementation raises the ceiling, Reduction "
        + "lowers it, Transfer moves budget out of SourceCompetency into Competency.")]
    BudgetAdjustmentType Type,
    [property: SwaggerSchema("Destination competency (`YYYY-MM`) — the month whose ceiling is raised, or "
        + "lowered on a Reduction.")]
    [property: SwaggerExample("2026-08")]
    string Competency,
    [property: SwaggerSchema("Source competency (`YYYY-MM`) the budget comes out of. Present only on a "
        + "Transfer.")]
    [property: SwaggerExample("2026-07")]
    string? SourceCompetency,
    [property: SwaggerSchema("Amount to move, in BRL. Always positive — the direction comes from Type, "
        + "so a Reduction is not a negative value.")]
    [property: SwaggerExample("25000.00")]
    decimal Value,
    [property: SwaggerSchema("Why the adjustment is needed — what triage and the approver read.")]
    [property: SwaggerExample("Remanejamento do saldo de treinamento para cobrir manutenção de equipamentos.")]
    string Justification,
    [property: SwaggerSchema("Free-text note on where the offsetting budget comes from (contrapartidas).")]
    string? Counterparts,
    [property: SwaggerSchema("Where the adjustment stands. The ceiling only changes once it is approved.")]
    BudgetAdjustmentStatus Status,
    [property: SwaggerSchema("The workflow history, oldest first.")]
    IReadOnlyList<BudgetAdjustmentTransitionResponse> Transitions,
    [property: SwaggerSchema("When it was opened.")]
    DateTimeOffset CreatedAt,
    [property: SwaggerSchema("Who opened it.")]
    [property: SwaggerExample("redacted-13@example.invalid")]
    string CreatedBy,
    [property: SwaggerSchema("When it entered the current status — the clock the stage SLA is measured "
        + "from.")]
    DateTimeOffset StatusChangedAt)
{
    /// <summary><paramref name="actors"/> names (and pictures) the transition authors on the timeline; omit
    /// it on the lists, where the timeline is not rendered.</summary>
    public static BudgetAdjustmentResponse From(
        BudgetAdjustment a, IReadOnlyDictionary<Guid, UserProfile>? actors = null) =>
        new(new BudgetAdjustmentId(a.Id), a.Code, new CompanyId(a.CompanyId), new UnitId(a.UnitId),
            new CostCenterId(a.CostCenterId), a.Type, a.Competency, a.SourceCompetency, a.Value,
            a.Justification, a.Counterparts, a.Status,
            a.Transitions.OrderBy(t => t.OccurredAt).Select(t => BudgetAdjustmentTransitionResponse.From(t, actors)).ToList(),
            a.CreatedAt, a.CreatedBy, a.StatusChangedAt);
}
