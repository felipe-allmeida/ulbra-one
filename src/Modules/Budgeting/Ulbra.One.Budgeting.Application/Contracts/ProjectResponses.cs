using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Budgeting.Application.Contracts;

/// <summary>Response shape for a <see cref="Project"/> (project with an accumulated spending ceiling).</summary>
[SwaggerSchema("A project with an accumulated spending ceiling. Unlike the cost center's budget, which "
    + "resets every month, a project's ceiling spans its whole life.")]
public sealed record ProjectResponse(
    ProjectId Id,
    [property: SwaggerSchema("Company the project belongs to.")]
    CompanyId CompanyId,
    [property: SwaggerSchema("Unit the project belongs to.")]
    UnitId UnitId,
    [property: SwaggerSchema("Project name. Unique within the company.")]
    [property: SwaggerExample("Reforma do bloco B")]
    string Name,
    [property: SwaggerSchema("What the project is about.")]
    string? Description,
    [property: SwaggerSchema("Accumulated ceiling, in BRL — for the whole project, not per month.")]
    [property: SwaggerExample("800000.00")]
    decimal BudgetLimit,
    [property: SwaggerSchema("Amount already spent before the project was registered here, in BRL. It is "
        + "counted against the ceiling, so a project migrated mid-flight starts with its history intact.")]
    [property: SwaggerExample("120000.00")]
    decimal InitialConsumedValue,
    [property: SwaggerSchema("Project status. A closed project cannot be reopened — that transition is "
        + "refused.")]
    ProjectStatus Status)
{
    public static ProjectResponse From(Project p) =>
        new(new ProjectId(p.Id), new CompanyId(p.CompanyId), new UnitId(p.UnitId),
            p.Name, p.Description, p.BudgetLimit, p.InitialConsumedValue, p.Status);
}

/// <summary>Accumulated tracking for a project: orçado × previsto × realizado × saldo.
/// <c>Saldo &lt; 0</c> (<see cref="IsOverCeiling"/>) means the ceiling is blown — the frontend paints it red
/// and the approval panel (AEL-790) raises the overrun warning.</summary>
[SwaggerSchema("A project's accumulated tracking: ceiling against what is committed and what has been "
    + "paid. Previsto includes Realizado — they are stages of the same money, not two amounts to add.")]
public sealed record ProjectMetricsResponse(
    ProjectId Id,
    [property: SwaggerSchema("Project name.")]
    [property: SwaggerExample("Reforma do bloco B")]
    string Name,
    [property: SwaggerSchema("Project status.")]
    ProjectStatus Status,
    [property: SwaggerSchema("Accumulated ceiling, in BRL.")]
    [property: SwaggerExample("800000.00")]
    decimal BudgetLimit,
    [property: SwaggerSchema("Committed, in BRL — everything the approved requisitions programmed, "
        + "including what has already been paid.")]
    [property: SwaggerExample("640000.00")]
    decimal Previsto,
    [property: SwaggerSchema("Paid, in BRL. A subset of Previsto.")]
    [property: SwaggerExample("410000.00")]
    decimal Realizado,
    [property: SwaggerSchema("Ceiling minus what is committed, in BRL. Negative means the ceiling is "
        + "blown — see IsOverCeiling.")]
    [property: SwaggerExample("160000.00")]
    decimal Saldo,
    [property: SwaggerSchema("Previsto as a percentage of the ceiling. Can exceed 100.")]
    [property: SwaggerExample("80.0")]
    decimal PercentPrevisto,
    [property: SwaggerSchema("Realizado as a percentage of the ceiling.")]
    [property: SwaggerExample("51.25")]
    decimal PercentRealizado,
    [property: SwaggerSchema("Whether the ceiling is blown (Saldo below zero). It does not block anything "
        + "on its own — the approval panel raises a warning and the decision stays with the approver.")]
    bool IsOverCeiling,
    [property: SwaggerSchema("How many requisitions are linked to the project.")]
    [property: SwaggerExample("23")]
    int RequisitionCount)
{
    public static ProjectMetricsResponse From(Project p, ProjectMetrics m, int requisitionCount) =>
        new(new ProjectId(p.Id), p.Name, p.Status, m.BudgetLimit, m.Previsto, m.Realizado, m.Saldo,
            m.PercentPrevisto, m.PercentRealizado, m.IsOverCeiling, requisitionCount);
}

/// <summary>Batch of <see cref="ProjectMetricsResponse"/> keyed by project id — one call for a whole project
/// list instead of one request per project.</summary>
[SwaggerSchema("Project metrics keyed by project id. Projects that do not exist are omitted.")]
public sealed record ProjectMetricsListResponse(
    [property: SwaggerSchema("Accumulated metrics per project, keyed by project id.")]
    IReadOnlyDictionary<string, ProjectMetricsResponse> Metrics);
