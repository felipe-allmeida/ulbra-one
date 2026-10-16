namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for accounting journal lines (wire form <c>jline_{guid:N}</c>).</summary>
[PrefixedId("jline_")]
public readonly partial record struct JournalLineId(Guid Value);
