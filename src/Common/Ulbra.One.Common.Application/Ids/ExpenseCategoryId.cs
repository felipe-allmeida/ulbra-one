namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for ExpenseCategory entities (wire form <c>expense_category_{guid:N}</c>).</summary>
[PrefixedId("expense_category_")]
public readonly partial record struct ExpenseCategoryId(Guid Value);
