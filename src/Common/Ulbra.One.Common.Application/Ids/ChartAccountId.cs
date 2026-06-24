namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for ChartAccount entities (wire form <c>chart_account_{guid:N}</c>).</summary>
[PrefixedId("chart_account_")]
public readonly partial record struct ChartAccountId(Guid Value);
