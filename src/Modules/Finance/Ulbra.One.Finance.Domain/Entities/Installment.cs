namespace Ulbra.One.Finance.Domain.Entities;

/// <summary>A single payable installment (parcela) — the unit of payment/baixa.</summary>
public class Installment
{
    private readonly List<Payment> _payments = new();

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid PayableId { get; private set; }
    public int Sequence { get; private set; }
    public DateOnly DueDate { get; private set; }
    public decimal Amount { get; private set; }
    public InstallmentStatus Status { get; private set; } = InstallmentStatus.Open;
    public decimal PaidAmount { get; private set; }

    public IReadOnlyList<Payment> Payments => _payments;

    private Installment() { }
    public Installment(int sequence, DateOnly dueDate, decimal amount)
    {
        Sequence = sequence;
        DueDate = dueDate;
        Amount = amount;
    }

    public Payment RegisterPayment(Payment payment)
    {
        if (Status == InstallmentStatus.Paid)
            throw new InvalidOperationException("Installment is already settled.");
        var remaining = Amount - PaidAmount;
        if (payment.PrincipalApplied > remaining + 0.005m)
            throw new InvalidOperationException("Payment principal exceeds the installment's outstanding balance.");
        _payments.Add(payment);
        Recompute();
        return payment;
    }

    public void ReversePayment(Guid paymentId, string? reason)
    {
        var payment = _payments.FirstOrDefault(p => p.Id == paymentId)
            ?? throw new InvalidOperationException("Payment not found on this installment.");
        payment.Reverse(reason);
        Recompute();
    }

    private void Recompute()
    {
        PaidAmount = _payments.Where(p => !p.IsReversed).Sum(p => p.PrincipalApplied);
        Status = PaidAmount <= 0 ? InstallmentStatus.Open
            : PaidAmount >= Amount ? InstallmentStatus.Paid
            : InstallmentStatus.PartiallyPaid;
    }
}
