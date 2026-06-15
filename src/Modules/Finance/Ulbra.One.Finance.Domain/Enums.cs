namespace Ulbra.One.Finance.Domain;

public enum TransactionType { Payable = 0, Receivable = 1 }

public enum TransactionStatus { Pending = 0, Approved = 1, Paid = 2, Rejected = 3 }

public enum PayableOrigin { Manual, Purchase, Payroll, SeniorImport }

public enum PayableStatus { PendingApproval, Approved, Rejected, Cancelled, Settled }

public enum InstallmentStatus { Open, PartiallyPaid, Paid }

public enum BankAccountType { Checking, Savings }

public enum PaymentMethod { Pix, Ted, Boleto, Cash, DirectDebit, Other }

public enum StatementSource { OfxUpload, CsvUpload }

public enum ImportStatus { Completed, Failed }

public enum TransactionDirection { Debit, Credit }

public enum MatchStatus { Unmatched, Suggested, Reconciled, Ignored }

public enum ChartType { Accounting, Financial }

public enum AccountKind { Synthetic, Analytical }

public enum AccountNature { Debit, Credit }

public enum AgingBucket { NotYetDue, Overdue1To30, Overdue31To60, Overdue61To90, Overdue90Plus }
