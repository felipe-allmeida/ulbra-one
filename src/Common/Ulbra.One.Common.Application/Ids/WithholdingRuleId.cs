namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for WithholdingRule entities (wire form <c>whrule_{guid:N}</c>).</summary>
[PrefixedId("whrule_")]
public readonly partial record struct WithholdingRuleId(Guid Value);
