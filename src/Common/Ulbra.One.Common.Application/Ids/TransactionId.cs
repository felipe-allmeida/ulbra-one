namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for Transaction entities (wire form <c>transaction_{guid:N}</c>).</summary>
[PrefixedId("transaction_")]
public readonly partial record struct TransactionId(Guid Value);
