namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for CostCenter entities (wire form <c>cost_center_{guid:N}</c>).</summary>
[PrefixedId("cost_center_")]
public readonly partial record struct CostCenterId(Guid Value);
