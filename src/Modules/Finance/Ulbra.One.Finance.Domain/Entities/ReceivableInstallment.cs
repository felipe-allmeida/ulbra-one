namespace Ulbra.One.Finance.Domain.Entities;

/// <summary>A single receivable installment (parcela/mensalidade) — the unit of receipt/baixa.</summary>
public class ReceivableInstallment
{
    private readonly List<Receipt> _receipts = new();

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ReceivableId { get; private set; }
    public int Sequence { get; private set; }
    public DateOnly DueDate { get; private set; }
    public decimal Amount { get; private set; }
    public ReceivableInstallmentStatus Status { get; private set; } = ReceivableInstallmentStatus.Open;
    public decimal ReceivedAmount { get; private set; }

    public IReadOnlyList<Receipt> Receipts => _receipts;

    private ReceivableInstallment() { }
    public ReceivableInstallment(int sequence, DateOnly dueDate, decimal amount)
    {
        Sequence = sequence;
        DueDate = dueDate;
        Amount = amount;
    }

    public Receipt RegisterReceipt(Receipt receipt)
    {
        if (Status == ReceivableInstallmentStatus.Received)
            throw new InvalidOperationException("Installment is already settled.");
        // Reject over-receipt: the principal applied cannot exceed the outstanding balance.
        if (ReceivedAmount + receipt.PrincipalApplied > Amount)
            throw new InvalidOperationException("Receipt would exceed the installment outstanding balance.");
        _receipts.Add(receipt);
        Recompute();
        return receipt;
    }

    public void ReverseReceipt(Guid receiptId, Guid reversedById, DateTimeOffset at, string? reason)
    {
        var receipt = _receipts.FirstOrDefault(r => r.Id == receiptId)
            ?? throw new InvalidOperationException("Receipt not found on this installment.");
        receipt.Reverse(reversedById, at, reason);
        Recompute();
    }

    private void Recompute()
    {
        ReceivedAmount = _receipts.Where(r => !r.IsReversed).Sum(r => r.PrincipalApplied);
        Status = ReceivedAmount <= 0 ? ReceivableInstallmentStatus.Open
            : ReceivedAmount >= Amount ? ReceivableInstallmentStatus.Received
            : ReceivableInstallmentStatus.PartiallyReceived;
    }
}
