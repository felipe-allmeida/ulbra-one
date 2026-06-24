namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for Receivable entities (wire form <c>receivable_{guid:N}</c>).</summary>
[PrefixedId("receivable_")]
public readonly partial record struct ReceivableId(Guid Value);
