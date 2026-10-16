namespace Ulbra.One.Finance.Domain;

public enum TransactionType { Payable = 0, Receivable = 1 }

public enum TransactionStatus { Pending = 0, Approved = 1, Paid = 2, Rejected = 3 }

/// <summary>Intake channel of a payable. Manual = filled by hand (NF registered later), FiscalDocument = born
/// from an uploaded NF file, Advance = adiantamento (paid before the NF exists — exempt from the NF gate).
/// Purchase/Payroll/SeniorImport are reserved for system integrations. Int-backed: append only, never reorder.</summary>
public enum PayableOrigin { Manual, Purchase, Payroll, SeniorImport, FiscalDocument, Advance }

/// <summary>Lifecycle of a payable. PendingTaxAnalysis (AEL: análise tributária) is a blocking stage between
/// approval and scheduling for Serviço/Conta Fixa titles: while parked here the payable can neither be paid nor
/// have its accounting incurral booked, until the Tributário validates the retenções and releases it. Int-backed:
/// append only, never reorder.</summary>
public enum PayableStatus { Received, PendingClassification, PendingApproval, PendingAccounting, Scheduled, Paid, Rejected, Cancelled, PendingTaxAnalysis }

/// <summary>Whether a payable is a Service (subject to source withholding) or a Product. Undetermined = null on the payable.</summary>
public enum PayableType { Service, Product }

public enum InstallmentStatus { Open, PartiallyPaid, Paid, InRemittance }

public enum BankAccountType { Checking, Savings }

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

/// <summary>What a payable attachment represents — Finance-local mirror of Budgeting's
/// <c>RequisitionAttachmentKind</c> (carried across the module boundary as a neutral string, no cross-module
/// reference). <see cref="FiscalDocument"/> is the only kind that satisfies the "NF presente" gate together with
/// a parsed <c>FiscalDocument</c>; <see cref="BankSlip"/> (boleto)/<see cref="Invoice"/> (fatura) are
/// payment/billing documents and <see cref="Other"/> is any supporting file. Persisted as a string; default
/// <see cref="Other"/>.</summary>
public enum PayableAttachmentKind { Other, FiscalDocument, BankSlip, Invoice }

/// <summary>Lifecycle of a CNAB payment remittance batch. Generated → (SP-3b-2) Confirmed; or Cancelled before sending.</summary>
public enum RemittanceStatus { Generated, Cancelled, Confirmed, PartiallyReturned }

/// <summary>Lifecycle of a single line within a remittance.</summary>
public enum RemittanceLineStatus { Pending, Paid, Returned }

/// <summary>Payment-form category of a remittance file (AEL-933). CNAB240 splits payments into these buckets —
/// PIX and TED each their own lote/segment, and boleto splits into bancário (Seg J/J-52) vs convênio/arrecadação
/// (Seg O) by the código de barras. As of Onda 6 the remittance is partitioned so one file carries one category.</summary>
public enum RemittanceCategory { Pix, Ted, BoletoBancario, Convenio }

/// <summary>Brazilian tax withholding types applicable to a payable (retenções na fonte).</summary>
public enum WithholdingTaxType { Irrf, Iss, Inss, Pis, Cofins, Csll, Other }

/// <summary>When a withholding's fato gerador occurs, hence when it is recognized in accounting:
/// <c>OnIssue</c> = at the document's competence (the incurral, keeping the issue date);
/// <c>OnPayment</c> = at each payment/baixa (proportional to the amount paid).</summary>
public enum WithholdingTiming { OnIssue, OnPayment }

/// <summary>Maps a withholding tax to the moment it is recognized in accounting. CSRF (PIS+COFINS+CSLL,
/// DARF 5952) has a single fato gerador — the payment (Lei 10.833/2003) — so it is retained at each baixa.
/// IRRF, INSS and ISS have their fato gerador at the document's emission/registro (competence), as does the
/// <c>Other</c> escape hatch by default. Mirrors <c>RetentionGroups.For</c> (Fiscal.Domain).</summary>
public static class WithholdingTimings
{
    public static WithholdingTiming For(WithholdingTaxType taxType) => taxType switch
    {
        WithholdingTaxType.Pis or WithholdingTaxType.Cofins or WithholdingTaxType.Csll => WithholdingTiming.OnPayment,
        _ => WithholdingTiming.OnIssue, // Irrf, Inss, Iss, Other → emissão/competência
    };
}
