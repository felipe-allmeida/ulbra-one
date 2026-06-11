namespace Ulbra.One.Finance.Domain;

public enum TransactionType { Payable = 0, Receivable = 1 }

public enum TransactionStatus { Pending = 0, Approved = 1, Paid = 2, Rejected = 3 }

public enum PayableOrigin { Manual, Purchase, Payroll, SeniorImport }

public enum PayableStatus { PendingApproval, Approved, Rejected, Cancelled, Settled }

public enum InstallmentStatus { Open, PartiallyPaid, Paid }

public enum BankAccountType { Checking, Savings }

public enum PaymentMethod { Pix, Ted, Boleto, Cash, DirectDebit, Other }

/// <summary>Where a receivable originates. AcademicImport is reserved for future Prime/Ensino ingestion.</summary>
public enum ReceivableOrigin { Manual, AcademicImport, Rent, MiscRevenue }

public enum ReceivableStatus { AwaitingReceipt, Received, Cancelled, WrittenOff }

public enum ReceivableInstallmentStatus { Open, PartiallyReceived, Received }

public enum StatementSource { OfxUpload, CsvUpload }

public enum ImportStatus { Completed, Failed }

public enum TransactionDirection { Debit, Credit }

public enum MatchStatus { Unmatched, Suggested, Reconciled, Ignored }

public enum ChartType { Accounting, Financial }

public enum AccountKind { Synthetic, Analytical }

public enum AccountNature { Debit, Credit }
