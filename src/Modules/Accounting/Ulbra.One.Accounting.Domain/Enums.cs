namespace Ulbra.One.Accounting.Domain;

/// <summary>Top-level classification of a ledger account, driving DRE/BP placement.</summary>
public enum AccountGroup { Asset, Liability, Equity, Revenue, Expense }

/// <summary>Synthetic accounts aggregate children; only analytical accounts receive entries.</summary>
public enum LedgerAccountKind { Synthetic, Analytical }

/// <summary>The side on which an account's balance normally increases.</summary>
public enum NormalBalance { Debit, Credit }

/// <summary>The side a journal line posts to.</summary>
public enum EntrySide { Debit, Credit }

/// <summary>Lifecycle of a journal entry. Posted/Reversed are immutable; corrections are made by reversal.</summary>
public enum JournalStatus { Draft, Posted, Reversed }

/// <summary>How a journal entry originated.</summary>
public enum JournalOrigin { Manual, Automatic }

/// <summary>What accounting event a <see cref="PendingAccountingPosting"/> represents.</summary>
public enum PostingKind { Incurral, Settlement, Reversal }

/// <summary>Lifecycle of an accounting period (competência mensal): Open accepts postings, Closed blocks
/// them (the closed-period guard), Closing is the transitional state used by the monthly-close routine.</summary>
public enum AccountingPeriodStatus { Open, Closing, Closed }
