namespace Ulbra.One.Finance.Domain.Entities;

/// <summary>A payment (baixa) recorded against an installment. Multiple allowed (partial). Reversible (estorno).</summary>
public class Payment
{
    private readonly List<PaymentWithholding> _withholdings = new();

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid InstallmentId { get; private set; }
    public Guid PayableId { get; private set; }
    public DateOnly PaymentDate { get; private set; }
    public decimal Amount { get; private set; }
    public Guid BankAccountId { get; private set; }
    public PaymentMethod Method { get; private set; }
    public decimal Interest { get; private set; }
    public decimal Fine { get; private set; }
    public decimal Discount { get; private set; }
    public string? Notes { get; private set; }
    public bool IsReversed { get; private set; }
    public string? ReversalReason { get; private set; }

    /// <summary>CSRF (PIS/COFINS/CSLL) retentions recognized at this baixa, prorated to the amount paid.</summary>
    public IReadOnlyList<PaymentWithholding> Withholdings => _withholdings;

    public string? ProofFileName { get; private set; }
    public string? ProofContentType { get; private set; }
    public string? ProofStoragePath { get; private set; }
    public long? ProofSizeBytes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public Guid CreatedById { get; private set; }

    /// <summary>Principal that counts toward settling the installment.</summary>
    public decimal PrincipalApplied => Amount - Interest - Fine + Discount;

    private Payment() { }

    public Payment(Guid installmentId, Guid payableId, DateOnly paymentDate, decimal amount, Guid bankAccountId,
        PaymentMethod method, decimal interest, decimal fine, decimal discount, string? notes, Guid createdById, DateTimeOffset createdAt)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Payment amount must be positive.");
        if (interest < 0) throw new ArgumentOutOfRangeException(nameof(interest), "Interest cannot be negative.");
        if (fine < 0) throw new ArgumentOutOfRangeException(nameof(fine), "Fine cannot be negative.");
        if (discount < 0) throw new ArgumentOutOfRangeException(nameof(discount), "Discount cannot be negative.");
        if (amount - interest - fine + discount < 0)
            throw new ArgumentException("Payment principal cannot be negative (interest and fine exceed amount plus discount).", nameof(amount));
        InstallmentId = installmentId;
        PayableId = payableId;
        PaymentDate = paymentDate;
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

    /// <summary>Sets the CSRF retentions recognized at this payment (already prorated by the caller). Zero-amount
    /// entries are ignored. Replaces any existing rows.</summary>
    public void SetWithholdings(IEnumerable<(WithholdingTaxType TaxType, decimal BaseAmount, decimal Amount)> items)
    {
        _withholdings.Clear();
        foreach (var (taxType, baseAmount, amount) in items)
            if (amount > 0)
                _withholdings.Add(new PaymentWithholding(Id, taxType, baseAmount, amount));
    }

    public void Reverse(string? reason)
    {
        if (IsReversed) throw new InvalidOperationException("Payment is already reversed.");
        IsReversed = true;
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
