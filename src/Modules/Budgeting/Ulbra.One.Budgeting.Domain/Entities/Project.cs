using Ulbra.One.Common.Domain.Auditing;

namespace Ulbra.One.Budgeting.Domain.Entities;

/// <summary>
/// A project with a spending ceiling. Unlike a monthly budget cell (Core's CostCenterUnitBudget), a project's
/// budget is <b>accumulated</b> across its whole life — requisitions link to it and their cost counts
/// against the ceiling regardless of competency. References company/unit by loose Guid (no
/// cross-schema FK), like the rest of the module.
/// </summary>
public sealed class Project : IAuditable
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CompanyId { get; private set; }
    public Guid UnitId { get; private set; }

    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    /// <summary>Spending ceiling (teto de gasto) for the whole project. Never negative.</summary>
    public decimal BudgetLimit { get; private set; }

    /// <summary>Spend that happened before the system went live. Counts toward both previsto and
    /// realizado so migrated projects don't understate consumption.</summary>
    public decimal InitialConsumedValue { get; private set; }

    public ProjectStatus Status { get; private set; } = ProjectStatus.Active;

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    private Project() { }

    public Project(
        Guid companyId, Guid unitId, string name, decimal budgetLimit,
        decimal initialConsumedValue = 0m, string? description = null)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (unitId == Guid.Empty) throw new ArgumentException("UnitId is required.", nameof(unitId));

        CompanyId = companyId;
        UnitId = unitId;
        Status = ProjectStatus.Active;

        SetDetails(name, budgetLimit, initialConsumedValue, description);
    }

    /// <summary>Updates the editable fields. Company/unit and <see cref="Status"/> are immutable here
    /// (status moves through <see cref="Finish"/>/<see cref="Cancel"/>).</summary>
    public void Update(string name, decimal budgetLimit, decimal initialConsumedValue, string? description = null)
        => SetDetails(name, budgetLimit, initialConsumedValue, description);

    // ---- Status ----------------------------------------------------------------------------------

    /// <summary>Closes the project. Only an active project can be finished.</summary>
    public void Finish() => TransitionTo(ProjectStatus.Finished, ProjectStatus.Active);

    /// <summary>Cancels the project. Allowed from active or finished.</summary>
    public void Cancel() => TransitionTo(ProjectStatus.Cancelled, ProjectStatus.Active, ProjectStatus.Finished);

    // ---- Tracking --------------------------------------------------------------------------------

    /// <summary>Accumulated metrics. <paramref name="requisitionCosts"/> = cost of the ENTIRE non-cancelled
    /// pipeline (the "forecast" projection); <paramref name="committedCosts"/> = cost of only the requisitions
    /// that reached payment (InPayment+) — this is what reduces the real BALANCE; <paramref name="paidAmounts"/>
    /// = actual payments (realized). Pre-system consumption (<see cref="InitialConsumedValue"/>) is real and
    /// counts in all three. Balance = ceiling − real consumption; the pipeline in triage/approval does not lower
    /// the balance, only the projection (AEL-844).</summary>
    public ProjectMetrics CalculateMetrics(
        IEnumerable<decimal> requisitionCosts, IEnumerable<decimal> committedCosts, IEnumerable<decimal> paidAmounts)
    {
        var previsto = requisitionCosts.Sum() + InitialConsumedValue;
        var committed = committedCosts.Sum() + InitialConsumedValue;
        var realizado = paidAmounts.Sum() + InitialConsumedValue;
        return new ProjectMetrics(BudgetLimit, previsto, realizado, BudgetLimit - committed);
    }

    // ---- Audit -----------------------------------------------------------------------------------

    public void MarkCreated(string userId, DateTimeOffset at) { CreatedBy = userId; CreatedAt = at; MarkUpdated(userId, at); }
    public void MarkUpdated(string userId, DateTimeOffset at) { UpdatedBy = userId; UpdatedAt = at; }

    // ---- Helpers ---------------------------------------------------------------------------------

    private void TransitionTo(ProjectStatus target, params ProjectStatus[] allowedFrom)
    {
        if (Array.IndexOf(allowedFrom, Status) < 0)
            throw new InvalidOperationException($"Cannot move a {Status} project to {target}.");
        Status = target;
    }

    private void SetDetails(string name, decimal budgetLimit, decimal initialConsumedValue, string? description)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        BudgetLimit = NormalizeAmount(budgetLimit, nameof(budgetLimit));
        InitialConsumedValue = NormalizeAmount(initialConsumedValue, nameof(initialConsumedValue));
    }

    private static decimal NormalizeAmount(decimal value, string paramName)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(paramName, $"{paramName} cannot be negative.");
        return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
