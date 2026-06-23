namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for FiscalDocument entities (wire form <c>fiscaldoc_{guid:N}</c>).</summary>
[PrefixedId("fiscaldoc_")]
public readonly partial record struct FiscalDocumentId(Guid Value);
