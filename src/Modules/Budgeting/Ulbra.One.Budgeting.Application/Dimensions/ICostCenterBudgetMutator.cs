namespace Ulbra.One.Budgeting.Application.Dimensions;

/// <summary>Outcome of a budget-cell mutation applied by a budget adjustment.</summary>
public enum BudgetMutationStatus
{
    Ok,
    CellNotFound,
    WouldGoNegative,
}

/// <summary>
/// Cross-module port (adapter in Infrastructure) that mutates the Core-owned monthly budget cells
/// (<c>CostCenterUnitBudget</c>) on behalf of a budget adjustment, without the Application layer referencing
/// Core types. Runs against the shared unit of work, so the mutation commits atomically with the adjustment's
/// status change. Mirrors the read-only <see cref="IBudgetDimensionLookup"/> pattern.
/// </summary>
public interface ICostCenterBudgetMutator
{
    /// <summary>Raises the cell's monthly ceiling by <paramref name="value"/>, creating it (seeded with the
    /// value) when absent — a supplement or a transfer destination. Always <see cref="BudgetMutationStatus.Ok"/>.</summary>
    Task<BudgetMutationStatus> IncreaseOrCreateAsync(
        Guid costCenterId, Guid unitId, Guid companyId, string competency, decimal value, CancellationToken ct);

    /// <summary>Lowers an existing cell's monthly ceiling by <paramref name="value"/> — a reduction or a
    /// transfer source. <see cref="BudgetMutationStatus.CellNotFound"/> when there is no cell,
    /// <see cref="BudgetMutationStatus.WouldGoNegative"/> when it would drive the ceiling below zero.</summary>
    Task<BudgetMutationStatus> DecreaseExistingAsync(
        Guid costCenterId, Guid unitId, string competency, decimal value, CancellationToken ct);
}
