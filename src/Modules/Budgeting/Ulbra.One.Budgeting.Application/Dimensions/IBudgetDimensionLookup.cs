namespace Ulbra.One.Budgeting.Application.Dimensions;

/// <summary>A company's cost center (id + display name) — the "natureza" axis of budget summaries.</summary>
public sealed record CostCenterInfo(Guid Id, string Name);

/// <summary>Why a (unit, cost center) pair is or isn't a valid budgeting dimension.</summary>
public enum DimensionStatus
{
    Ok,
    UnitNotFound,
    CostCenterNotFound,
    CompanyMismatch,
    CostCenterInactive,
    UnitNotInCostCenter,
}

/// <summary>Outcome of resolving a budget dimension. On <see cref="DimensionStatus.Ok"/>, <see cref="CompanyId"/>
/// carries the company derived from the Unit (the stored, authoritative company for the budget).</summary>
public sealed record DimensionResult(DimensionStatus Status, Guid CompanyId)
{
    public bool IsValid => Status == DimensionStatus.Ok;
}

/// <summary>
/// Cross-module port (adapter in Infrastructure) that validates a budget's dimensions against the Core
/// module without the Application layer referencing Core types. Checks that the unit and cost center
/// exist, share a company, the cost center is active, and the unit is associated with the cost center
/// (CostCenterUnit) — and returns the unit's company id.
/// </summary>
public interface IBudgetDimensionLookup
{
    Task<DimensionResult> ResolveAsync(Guid unitId, Guid costCenterId, CancellationToken ct);

    /// <summary>Resolves just the unit (projects carry no cost center), returning its company id.
    /// Yields <see cref="DimensionStatus.UnitNotFound"/> when the unit doesn't exist.</summary>
    Task<DimensionResult> ResolveUnitAsync(Guid unitId, CancellationToken ct);

    /// <summary>Active cost centers of a company (id + name), ordered by name — the naturezas of a budget summary.</summary>
    Task<IReadOnlyList<CostCenterInfo>> ListActiveCostCentersAsync(Guid companyId, CancellationToken ct);
}
