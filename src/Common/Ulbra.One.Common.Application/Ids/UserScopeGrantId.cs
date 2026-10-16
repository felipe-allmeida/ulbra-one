namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for UserScopeGrant entities (wire form <c>usg_{guid:N}</c>).</summary>
[PrefixedId("usg_")]
public readonly partial record struct UserScopeGrantId(Guid Value);
