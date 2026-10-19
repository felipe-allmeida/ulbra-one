namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for BudgetAdjustment (budget adjustment) entities
/// (wire form <c>budgetadj_{guid:N}</c>).</summary>
[PrefixedId("budgetadj_")]
public readonly partial record struct BudgetAdjustmentId(Guid Value);
