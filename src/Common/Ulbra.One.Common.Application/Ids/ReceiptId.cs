namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for Receipt entities (wire form <c>receipt_{guid:N}</c>).</summary>
[PrefixedId("receipt_")]
public readonly partial record struct ReceiptId(Guid Value);
