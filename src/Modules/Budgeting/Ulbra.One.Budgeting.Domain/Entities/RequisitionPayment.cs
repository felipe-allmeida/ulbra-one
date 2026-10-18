namespace Ulbra.One.Budgeting.Domain.Entities;

/// <summary>A real payment (baixa) applied to a <see cref="Requisition"/>, fed back from the Finance module
/// when a payment is registered against the generated Payable (AEL-793). This is the "realizado" source that
/// replaces the estimate-based proxy: the requisition's realizado is the sum of its non-reversed payments.
/// <see cref="PaymentId"/>/<see cref="PayableId"/> are loose Guids into Finance (no cross-schema FK), and
/// <see cref="PaymentId"/> is the dedup key that makes the at-least-once integration idempotent.</summary>
public class RequisitionPayment
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid RequisitionId { get; private set; }

    /// <summary>The Finance <c>Payment</c> id — unique per baixa; the idempotency/dedup key.</summary>
    public Guid PaymentId { get; private set; }

    /// <summary>The Finance <c>Payable</c> the payment settled (audit / traceability).</summary>
    public Guid PayableId { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>Competency ("YYYY-MM") the payment contributes to — the paid installment's due-date month,
    /// so realizado buckets line up with the estimate's per-competency contribution.</summary>
    public string Competency { get; private set; } = string.Empty;

    public DateTimeOffset PaidAt { get; private set; }

    /// <summary>Whether the underlying Finance payment was reversed (estornado); reversed rows are excluded
    /// from realizado but kept for the trail.</summary>
    public bool Reversed { get; private set; }

    private RequisitionPayment() { }

    public RequisitionPayment(Guid requisitionId, Guid paymentId, Guid payableId, decimal amount, string competency, DateTimeOffset paidAt)
    {
        RequisitionId = requisitionId;
        PaymentId = paymentId;
        PayableId = payableId;
        Amount = amount;
        Competency = competency;
        PaidAt = paidAt;
    }

    public void Reverse() => Reversed = true;
}
