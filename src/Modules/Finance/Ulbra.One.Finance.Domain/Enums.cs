namespace Ulbra.One.Finance.Domain;

public enum TransactionType { Payable = 0, Receivable = 1 }

public enum TransactionStatus { Pending = 0, Approved = 1, Paid = 2, Rejected = 3 }

public enum PayableOrigin { Manual, Purchase, Payroll, SeniorImport }

public enum PayableStatus { Received, PendingClassification, PendingApproval, PendingAccounting, Scheduled, Paid, Rejected, Cancelled }

/// <summary>Whether a payable is a Service (subject to source withholding) or a Product. Undetermined = null on the payable.</summary>
public enum PayableType { Service, Product }

public enum InstallmentStatus { Open, PartiallyPaid, Paid, InRemittance }

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

public enum AgingBucket { NotYetDue, Overdue1To30, Overdue31To60, Overdue61To90, Overdue90Plus }

/// <summary>Concrete source format of a fiscal document attached to a payable.</summary>
public enum FiscalDocumentFormat { NfeXml, NfseXml, Pdf, Image }

/// <summary>Lifecycle of a CNAB payment remittance batch. Generated → (SP-3b-2) Confirmed; or Cancelled before sending.</summary>
public enum RemittanceStatus { Generated, Cancelled, Confirmed, PartiallyReturned }

/// <summary>Lifecycle of a single line within a remittance.</summary>
public enum RemittanceLineStatus { Pending, Paid, Returned }

/// <summary>Brazilian tax withholding types applicable to a payable (retenções na fonte).</summary>
public enum WithholdingTaxType { Irrf, Iss, Inss, Pis, Cofins, Csll, Other }
