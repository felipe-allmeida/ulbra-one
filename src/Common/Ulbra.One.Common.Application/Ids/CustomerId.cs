namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for Customer entities (wire form <c>customer_{guid:N}</c>).</summary>
[PrefixedId("customer_")]
public readonly partial record struct CustomerId(Guid Value);
