namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for BankTransaction entities (wire form <c>bank_txn_{guid:N}</c>).</summary>
[PrefixedId("bank_txn_")]
public readonly partial record struct BankTransactionId(Guid Value);
