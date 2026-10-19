using Ulbra.One.Common.Domain;
using Ulbra.One.Common.Domain.Validation;

namespace Ulbra.One.Budgeting.Domain.Entities;

/// <summary>One manually-launched monthly cycle of a recurring requisition (Conta Fixa) — the
/// invoice for a given competency, added after approval WITHOUT re-running the esteira (AEL-877). Each cycle
/// materializes its own Payable in Finance (the "fluxo Payable"); the base cycle (the approval Payable) is NOT
/// a row here — it lives on the requisition as <see cref="Requisition.BasePayableId"/>. Mirrors the legacy
/// app's per-month payments (variable amount each month).</summary>
public class RequisitionCycle
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid RequisitionId { get; private set; }

    /// <summary>1-based launch order.</summary>
    public int Sequence { get; private set; }

    /// <summary>Competency this invoice belongs to (<c>"YYYY-MM"</c>) — unique per requisition and strictly
    /// after the start competency (the base cycle owns the start).</summary>
    public string Competency { get; private set; } = string.Empty;

    /// <summary>Invoice amount for the month (varies month to month); greater than zero, 2 decimals.</summary>
    public decimal Amount { get; private set; }

    public DateOnly DueDate { get; private set; }
    public PaymentMethod PaymentMethod { get; private set; }

    /// <summary>Boleto linha digitável when paid by boleto; null otherwise. Carried to the cycle's Payable.</summary>
    public string? BoletoDigitableLine { get; private set; }

    /// <summary>Per-payment PIX key override (AEL-938) when this cycle's invoice is paid by PIX; null otherwise.
    /// Carried to the cycle's Payable and used by the CNAB240 Segmento B in place of the vendor's PIX key.</summary>
    public string? PixKeyOverride { get; private set; }

    /// <summary>Per-payment PIX "copia e cola" payload (EMV/BR Code, AEL-963) when this cycle's invoice is paid
    /// by a PIX QR; null otherwise. Carried to the cycle's Payable; decoded by CNAB240 generation.</summary>
    public string? PixPayloadEmv { get; private set; }

    /// <summary>The Finance Payable materialized for this cycle — filled by the return callback (AEL-877, A4b);
    /// null until Finance confirms creation.</summary>
    public Guid? PayableId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;

    private RequisitionCycle() { }

    public RequisitionCycle(Guid requisitionId, int sequence, string competency, decimal amount, DateOnly dueDate,
        PaymentMethod paymentMethod, string? boletoDigitableLine, Guid actorId, DateTimeOffset at, string? pixKeyOverride = null, string? pixPayloadEmv = null)
    {
        if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence), "Sequence must be a positive number.");
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Cycle amount must be greater than zero.");

        RequisitionId = requisitionId;
        Sequence = sequence;
        Competency = competency;
        Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        DueDate = dueDate;
        PaymentMethod = paymentMethod;
        BoletoDigitableLine = BoletoLine.Canonical(boletoDigitableLine);
        (PixKeyOverride, PixPayloadEmv) = PixEntry.Resolve(pixKeyOverride, pixPayloadEmv);
        CreatedAt = at;
        CreatedBy = actorId.ToString();
    }

    /// <summary>Links the Finance Payable created for this cycle (idempotent — first wins).</summary>
    public void LinkPayable(Guid payableId) => PayableId ??= payableId;
}
