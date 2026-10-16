namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for Employee entities (wire form <c>employee_{guid:N}</c>).</summary>
[PrefixedId("employee_")]
public readonly partial record struct EmployeeId(Guid Value);
