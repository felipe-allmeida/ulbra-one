namespace Ulbra.One.Finance.Domain.Entities;

/// <summary>A single normalized bank-statement line, ingested from a source (OFX). Matched against installments.</summary>
public class BankTransaction
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ImportId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BankAccountId { get; private set; }
    public string ExternalId { get; private set; } = string.Empty;
    public DateOnly Date { get; private set; }
    public decimal Amount { get; private set; }
    public TransactionDirection Direction { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public MatchStatus MatchStatus { get; private set; } = MatchStatus.Unmatched;
    public Guid? SuggestedInstallmentId { get; private set; }
    public Guid? MatchedPaymentId { get; private set; }

    /// <summary>
    /// True when the reconciliation merely LINKED a payment that already existed (e.g. a baixa applied by the
    /// CNAB return), instead of creating one through the conciliation itself. It is the flag that keeps
    /// "desfazer conciliação" safe: undoing such a link must only unlink, never reverse a payment the bank
    /// really made. False for the conciliation-created path, whose undo does reverse.
    /// </summary>
    public bool PaymentPreexisting { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private BankTransaction() { }

    public BankTransaction(Guid importId, Guid companyId, Guid bankAccountId, string externalId, DateOnly date,
        decimal amount, string description, DateTimeOffset createdAt)
    {
        ImportId = importId;
        CompanyId = companyId;
        BankAccountId = bankAccountId;
        ExternalId = externalId;
        Date = date;
        Amount = amount;
        Direction = amount < 0 ? TransactionDirection.Debit : TransactionDirection.Credit;
        Description = description;
        CreatedAt = createdAt;
    }

    public void Suggest(Guid installmentId) { MatchStatus = MatchStatus.Suggested; SuggestedInstallmentId = installmentId; }
    public void ClearSuggestion() { if (MatchStatus == MatchStatus.Suggested) { MatchStatus = MatchStatus.Unmatched; SuggestedInstallmentId = null; } }

    public void Reconcile(Guid paymentId)
    {
        if (MatchStatus == MatchStatus.Reconciled) throw new InvalidOperationException("Transaction is already reconciled.");
        MatchStatus = MatchStatus.Reconciled;
        MatchedPaymentId = paymentId;
        PaymentPreexisting = false;
    }

    /// <summary>
    /// Ties this statement line to a payment that was ALREADY registered (typically a CNAB-return baixa), without
    /// creating anything. The installment is already settled, so it never enters the open-installment matcher —
    /// this is the only way such a debit stops reading as "não conciliado" forever.
    /// </summary>
    public void LinkExistingPayment(Guid paymentId)
    {
        if (MatchStatus == MatchStatus.Reconciled) throw new InvalidOperationException("Transaction is already reconciled.");
        MatchStatus = MatchStatus.Reconciled;
        MatchedPaymentId = paymentId;
        PaymentPreexisting = true;
    }

    public void Unreconcile()
    {
        MatchStatus = MatchStatus.Unmatched;
        MatchedPaymentId = null;
        SuggestedInstallmentId = null;
        PaymentPreexisting = false;
    }
    public void Ignore() { MatchStatus = MatchStatus.Ignored; SuggestedInstallmentId = null; }
}
