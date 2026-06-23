namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for accounting journal entries (wire form <c>journal_{guid:N}</c>).</summary>
[PrefixedId("journal_")]
public readonly partial record struct JournalEntryId(Guid Value);
