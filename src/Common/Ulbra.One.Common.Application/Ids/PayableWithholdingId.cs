namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for PayableWithholding entities (wire form <c>withholding_{guid:N}</c>).</summary>
[PrefixedId("withholding_")]
public readonly partial record struct PayableWithholdingId(Guid Value);
