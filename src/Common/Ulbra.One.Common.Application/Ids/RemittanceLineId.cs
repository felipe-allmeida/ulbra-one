namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for RemittanceLine entities (wire form <c>remittance_line_{guid:N}</c>).</summary>
[PrefixedId("remittance_line_")]
public readonly partial record struct RemittanceLineId(Guid Value);
