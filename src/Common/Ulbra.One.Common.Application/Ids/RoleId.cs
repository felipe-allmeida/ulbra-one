namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for Role (cargo) entities (wire form <c>role_{guid:N}</c>).</summary>
[PrefixedId("role_")]
public readonly partial record struct RoleId(Guid Value);
