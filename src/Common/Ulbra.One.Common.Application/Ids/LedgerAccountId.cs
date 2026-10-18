namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for accounting ledger accounts (wire form <c>ledgacct_{guid:N}</c>).</summary>
[PrefixedId("ledgacct_")]
public readonly partial record struct LedgerAccountId(Guid Value);
