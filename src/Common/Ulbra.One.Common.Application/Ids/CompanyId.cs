namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for Company (wire form <c>company_{guid:N}</c>).</summary>
[PrefixedId("company_")]
public readonly partial record struct CompanyId(Guid Value);
