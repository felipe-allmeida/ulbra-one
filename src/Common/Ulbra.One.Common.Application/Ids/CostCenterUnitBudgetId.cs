namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for a per-unit monthly budget cell owned by a CostCenter
/// (wire form <c>unit_budget_{guid:N}</c>).</summary>
[PrefixedId("unit_budget_")]
public readonly partial record struct CostCenterUnitBudgetId(Guid Value);
