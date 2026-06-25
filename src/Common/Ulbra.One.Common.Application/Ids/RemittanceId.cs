namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for Remittance entities (wire form <c>remittance_{guid:N}</c>).</summary>
[PrefixedId("remittance_")]
public readonly partial record struct RemittanceId(Guid Value);
