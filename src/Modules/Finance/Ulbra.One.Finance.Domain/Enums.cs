namespace Ulbra.One.Finance.Domain;

public enum TransactionType { Payable = 0, Receivable = 1 }

public enum TransactionStatus { Pending = 0, Approved = 1, Paid = 2, Rejected = 3 }
