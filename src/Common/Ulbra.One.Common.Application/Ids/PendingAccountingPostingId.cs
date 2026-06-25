namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for pending accounting postings (wire form <c>pendpost_{guid:N}</c>).</summary>
[PrefixedId("pendpost_")]
public readonly partial record struct PendingAccountingPostingId(Guid Value);
