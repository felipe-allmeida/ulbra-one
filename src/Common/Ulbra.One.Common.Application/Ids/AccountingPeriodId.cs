namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for accounting periods (wire form <c>acctperiod_{guid:N}</c>).</summary>
[PrefixedId("acctperiod_")]
public readonly partial record struct AccountingPeriodId(Guid Value);
