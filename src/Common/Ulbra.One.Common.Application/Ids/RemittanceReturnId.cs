namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for RemittanceReturn entities (wire form <c>remittance_return_{guid:N}</c>).</summary>
[PrefixedId("remittance_return_")]
public readonly partial record struct RemittanceReturnId(Guid Value);
