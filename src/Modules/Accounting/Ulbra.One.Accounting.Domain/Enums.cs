namespace Ulbra.One.Accounting.Domain;

/// <summary>Top-level classification of a ledger account, driving DRE/BP placement.</summary>
public enum AccountGroup { Asset, Liability, Equity, Revenue, Expense }

/// <summary>The line of the structured income statement (DRE) a result account rolls up to, in cascade order.
/// Null on non-result accounts (Asset/Liability/Equity). Assigned to each account (defaulted from the chart at
/// seed, editable in the ledger-account CRUD) and consumed by <c>IncomeStatementCalculator</c> to build the
/// cascade: gross revenue → deductions → net revenue → COGS → gross profit → operating expenses → financial
/// result → pre-tax result (LAIR) → (−)IRPJ (−)CSLL → net income.</summary>
public enum DreLine
{
    GrossRevenue,         // gross operating revenue
    RevenueDeductions,    // (−) revenue deductions (contra-revenue)
    CostOfSales,          // (−) cost of goods/services sold (CMV/CSV)
    OtherOperatingIncome, // other operating revenue
    OperatingExpenses,    // (−) operating expenses
    FinancialRevenue,     // financial revenue
    FinancialExpense,     // (−) financial expenses
    Other,                // other results (discontinued ops etc.) — compose the pre-tax result (LAIR)
    IncomeTax,            // (−) income tax provision (IRPJ)
    SocialContribution,   // (−) social contribution provision (CSLL)
}

/// <summary>Synthetic accounts aggregate children; only analytical accounts receive entries.</summary>
public enum LedgerAccountKind { Synthetic, Analytical }

/// <summary>The side on which an account's balance normally increases.</summary>
public enum NormalBalance { Debit, Credit }

/// <summary>The side a journal line posts to.</summary>
public enum EntrySide { Debit, Credit }

/// <summary>Lifecycle of a journal entry. Posted/Reversed are immutable; corrections are made by reversal.</summary>
public enum JournalStatus { Draft, Posted, Reversed }

/// <summary>How a journal entry originated. <c>Closing</c> marks the year-end result-clearing entries
/// (apuração + transferência ao PL); the DRE excludes them so they don't collapse the result.</summary>
public enum JournalOrigin { Manual, Automatic, Closing }

/// <summary>What accounting event a <see cref="PendingAccountingPosting"/> represents.</summary>
public enum PostingKind { Incurral, Settlement, Reversal }

/// <summary>Lifecycle of an accounting period (competência mensal): Open accepts postings, Closed blocks
/// them (the closed-period guard), Closing is the transitional state used by the monthly-close routine.</summary>
public enum AccountingPeriodStatus { Open, Closing, Closed }
