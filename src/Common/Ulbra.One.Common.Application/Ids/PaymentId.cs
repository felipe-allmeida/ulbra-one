namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for Payment entities (wire form <c>payment_{guid:N}</c>).</summary>
[PrefixedId("payment_")]
public readonly partial record struct PaymentId(Guid Value);
