using Ulbra.One.Common.Domain;

namespace Ulbra.One.Budgeting.Domain.Entities;

/// <summary>One authored installment of a requisition's payment plan, captured in the financial-complement
/// step (AEL-844): a due date, the value to transfer and the payment method. Once authored, these replace the
/// ephemeral <see cref="Requisition.BuildPaymentSchedule"/> as the source of truth — the generated Payable
/// materializes exactly these. Amounts sum to the requisition's total.</summary>
public class RequisitionInstallment
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid RequisitionId { get; private set; }

    /// <summary>1-based position in the plan (assigned by the aggregate on submit).</summary>
    public int Sequence { get; private set; }

    public DateOnly DueDate { get; private set; }

    /// <summary>Value to transfer for this installment; greater than zero, 2 decimals.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Payment method authored for this installment; carried through to the Finance installment.</summary>
    public PaymentMethod PaymentMethod { get; private set; }

    /// <summary>Boleto linha digitável (47 díg bancário / 48 díg convênio) authored when this installment is paid
    /// by boleto; null otherwise. Carried through to the Finance installment and consumed by the CNAB240
    /// Segmento J/O generation (convênio detected by the leading '8' of the barcode).</summary>
    public string? BoletoDigitableLine { get; private set; }

    /// <summary>Per-payment PIX key override (AEL-938) when this installment is paid by PIX; null otherwise.
    /// Carried to the Finance installment and used by the CNAB240 Segmento B instead of the vendor's registered key.</summary>
    public string? PixKeyOverride { get; private set; }

    private RequisitionInstallment() { }

    public RequisitionInstallment(Guid requisitionId, int sequence, DateOnly dueDate, decimal amount, PaymentMethod paymentMethod, string? boletoDigitableLine = null, string? pixKeyOverride = null)
    {
        if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence), "Sequence must be a positive number.");
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Installment amount must be greater than zero.");

        RequisitionId = requisitionId;
        Sequence = sequence;
        DueDate = dueDate;
        Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        PaymentMethod = paymentMethod;
        BoletoDigitableLine = string.IsNullOrWhiteSpace(boletoDigitableLine) ? null : boletoDigitableLine.Trim();
        PixKeyOverride = string.IsNullOrWhiteSpace(pixKeyOverride) ? null : pixKeyOverride.Trim();
    }
}
