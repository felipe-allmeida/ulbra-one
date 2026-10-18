namespace Ulbra.One.Finance.Domain.Entities;

/// <summary>A receipt (baixa de entrada) recorded against a receivable installment. Multiple allowed (partial).
/// Reversible (estorno). Records full authorship on both creation and reversal.</summary>
public class Receipt
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid InstallmentId { get; private set; }
    public Guid ReceivableId { get; private set; }
    public DateOnly ReceiptDate { get; private set; }
    public decimal Amount { get; private set; }
    public Guid BankAccountId { get; private set; }
    public PaymentMethod Method { get; private set; }
    public decimal Interest { get; private set; }
    public decimal Fine { get; private set; }
    public decimal Discount { get; private set; }
    public string? Notes { get; private set; }
    public bool IsReversed { get; private set; }
    public string? ReversalReason { get; private set; }

    public string? ProofFileName { get; private set; }
    public string? ProofContentType { get; private set; }
    public string? ProofStoragePath { get; private set; }
    public long? ProofSizeBytes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public Guid CreatedById { get; private set; }
    public DateTimeOffset? ReversedAt { get; private set; }
    public Guid? ReversedById { get; private set; }

    /// <summary>Principal that counts toward settling the installment (interest/fine do not reduce it; discount does).</summary>
    public decimal PrincipalApplied => Amount - Interest - Fine + Discount;

    private Receipt() { }

    public Receipt(Guid installmentId, Guid receivableId, DateOnly receiptDate, decimal amount, Guid bankAccountId,
        PaymentMethod method, decimal interest, decimal fine, decimal discount, string? notes, Guid createdById, DateTimeOffset createdAt)
    {
        InstallmentId = installmentId;
        ReceivableId = receivableId;
        ReceiptDate = receiptDate;
        Amount = amount;
        BankAccountId = bankAccountId;
        Method = method;
        Interest = interest;
        Fine = fine;
        Discount = discount;
        Notes = notes;
        CreatedById = createdById;
        CreatedAt = createdAt;
    }

    public void Reverse(Guid reversedById, DateTimeOffset at, string? reason)
    {
        if (IsReversed) throw new InvalidOperationException("Receipt is already reversed.");
        IsReversed = true;
        ReversedById = reversedById;
        ReversedAt = at;
        ReversalReason = reason;
    }

    public void SetProof(string fileName, string contentType, string storagePath, long sizeBytes)
    {
        ProofFileName = fileName;
        ProofContentType = contentType;
        ProofStoragePath = storagePath;
        ProofSizeBytes = sizeBytes;
    }
}
