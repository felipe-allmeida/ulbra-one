namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for BankAccount entities (wire form <c>bank_account_{guid:N}</c>).</summary>
[PrefixedId("bank_account_")]
public readonly partial record struct BankAccountId(Guid Value);
