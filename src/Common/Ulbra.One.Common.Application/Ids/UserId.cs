namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for User entities (wire form <c>user_{guid:N}</c>).</summary>
[PrefixedId("user_")]
public readonly partial record struct UserId(Guid Value);
