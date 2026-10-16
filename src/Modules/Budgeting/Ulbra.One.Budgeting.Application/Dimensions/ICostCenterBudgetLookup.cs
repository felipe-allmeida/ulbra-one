namespace Ulbra.One.Budgeting.Application.Dimensions;

/// <summary>An active monthly budget cell owned by a cost center (Core's <c>CostCenterUnitBudget</c>),
/// flattened to primitives so the Application layer stays free of Core types.</summary>
public sealed record CostCenterBudgetCell(string Competency, decimal MonthlyLimit);

/// <summary>Total active budget (orçado) of a cost center in a single competency, summed over units.</summary>
public sealed record CostCenterOrcado(Guid CostCenterId, decimal Orcado);

/// <summary>Cross-module read port (adapter in Infrastructure) that reads the Core-owned monthly budget cells
/// for a (cost center, unit) over a competency range. Feeds the authoritative budget control. Mirrors the
/// <see cref="IBudgetDimensionLookup"/> pattern.</summary>
public interface ICostCenterBudgetLookup
{
    /// <summary>Active cells for (costCenter, unit) whose competency is within the inclusive range
    /// <c>[from, to]</c> ("YYYY-MM").</summary>
    Task<IReadOnlyList<CostCenterBudgetCell>> GetCellsAsync(
        Guid costCenterId, Guid unitId, string from, string to, CancellationToken ct);

    /// <summary>Active budget (orçado) for a company's cost centers in a single competency, one row per cost
    /// center that has a cell. When <paramref name="unitIds"/> is given only those units' cells count; when null
    /// cells are summed across all units of the cost center.</summary>
    Task<IReadOnlyList<CostCenterOrcado>> SumOrcadoByCostCenterAsync(
        Guid companyId, IReadOnlyCollection<Guid>? unitIds, string competency, CancellationToken ct);
}
