namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for Payable entities (wire form <c>payable_{guid:N}</c>).</summary>
[PrefixedId("payable_")]
public readonly partial record struct PayableId(Guid Value);
