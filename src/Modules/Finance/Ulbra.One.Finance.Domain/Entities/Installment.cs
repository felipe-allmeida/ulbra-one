namespace Ulbra.One.Finance.Domain.Entities;

/// <summary>A single payable installment (parcela) — the unit of payment/baixa.</summary>
public class Installment
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid PayableId { get; private set; }
    public int Sequence { get; private set; }
    public DateOnly DueDate { get; private set; }
    public decimal Amount { get; private set; }
    public InstallmentStatus Status { get; private set; } = InstallmentStatus.Open;
    public decimal PaidAmount { get; private set; }

    private Installment() { }
    public Installment(int sequence, DateOnly dueDate, decimal amount)
    {
        Sequence = sequence;
        DueDate = dueDate;
        Amount = amount;
    }
}
