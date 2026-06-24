namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for Unit entities (wire form <c>unit_{guid:N}</c>).</summary>
[PrefixedId("unit_")]
public readonly partial record struct UnitId(Guid Value);
