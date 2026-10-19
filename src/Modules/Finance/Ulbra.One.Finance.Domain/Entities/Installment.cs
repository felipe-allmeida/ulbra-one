using Ulbra.One.Common.Domain;
using Ulbra.One.Common.Domain.Validation;

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
    public decimal NetAmount { get; private set; }
    public InstallmentStatus Status { get; private set; } = InstallmentStatus.Open;
    public decimal PaidAmount { get; private set; }
    public DateOnly ScheduledPaymentDate { get; private set; }
    public bool IsHeld { get; private set; }
    public string? HoldReason { get; private set; }

    /// <summary>Payment method authored for this installment by the requisition (AEL-844). Null for
    /// installments created before the feature, from a fiscal document, or without an authored method.</summary>
    public PaymentMethod? PaymentMethod { get; private set; }

    /// <summary>
    /// Boleto/arrecadação linha digitável (47 díg bancário / 48 díg convênio) when this installment
    /// is paid by boleto. Null for PIX/TED. Consumed by the CNAB240 Segmento J/O generation.
    /// </summary>
    public string? BoletoDigitableLine { get; private set; }

    /// <summary>
    /// Per-payment PIX key override (AEL-938): when set, the CNAB240 Segmento B is written with this key
    /// instead of the vendor's registered PIX key — for THIS payment only. Null/blank → the vendor's key is
    /// used (the vendor's registered key is never altered).
    /// </summary>
    public string? PixKeyOverride { get; private set; }

    /// <summary>
    /// Per-payment PIX "copia e cola" payload (EMV/BR Code) when this installment is paid by a PIX QR
    /// (AEL-963). Set instead of <see cref="PixKeyOverride"/> — a dynamic QR carries a payload URL that the
    /// CNAB240 forma 47 (Seg J-PIX + J52-PIX) transports; a static QR decodes to a key on forma 45. Stored
    /// verbatim (trim only, never normalized). Null/blank → this is not a copia-e-cola payment.
    /// </summary>
    public string? PixPayloadEmv { get; private set; }

    public IReadOnlyList<Payment> Payments => _payments;

    private Installment() { }
    public Installment(int sequence, DateOnly dueDate, decimal amount, string? boletoDigitableLine = null, PaymentMethod? paymentMethod = null, string? pixKeyOverride = null, string? pixPayloadEmv = null)
    {
        Sequence = sequence;
        DueDate = dueDate;
        Amount = amount;
        NetAmount = amount;
        ScheduledPaymentDate = dueDate;
        BoletoDigitableLine = BoletoLine.Canonical(boletoDigitableLine);
        PaymentMethod = paymentMethod;
        (PixKeyOverride, PixPayloadEmv) = PixEntry.Resolve(pixKeyOverride, pixPayloadEmv);
    }

    internal void SetNetAmount(decimal net) => NetAmount = net;

    public void SetScheduledPaymentDate(DateOnly date) => ScheduledPaymentDate = date;

    /// <summary>Sets/clears the boleto linha digitável (null/blank clears it).</summary>
    public void SetBoletoDigitableLine(string? line) => BoletoDigitableLine = BoletoLine.Canonical(line);

    /// <summary>Sets/clears the per-payment PIX key override (null/blank clears it → the vendor's key is used).</summary>
    public void SetPixKeyOverride(string? key) =>
        PixKeyOverride = string.IsNullOrWhiteSpace(key) ? null : key.Trim();

    /// <summary>Sets/clears the per-payment PIX copia-e-cola payload (null/blank clears it). Trim only.</summary>
    public void SetPixPayloadEmv(string? payload) =>
        PixPayloadEmv = string.IsNullOrWhiteSpace(payload) ? null : payload.Trim();

    /// <summary>Pauses an open installment so the daily remittance sweep skips it.</summary>
    public void Hold(string reason)
    {
        if (Status != InstallmentStatus.Open)
            throw new InvalidOperationException($"Only an open installment can be held (was {Status}).");
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Hold reason is required.");
        IsHeld = true;
        HoldReason = reason;
    }

    public void ReleaseHold()
    {
        IsHeld = false;
        HoldReason = null;
    }

    public Payment RegisterPayment(Payment payment)
    {
        if (Status == InstallmentStatus.Paid)
            throw new InvalidOperationException("Installment is already settled.");
        var remaining = NetAmount - PaidAmount;
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

    /// <summary>Locks an open installment into a payment remittance (prevents double-remittance).
    /// Reversed by <see cref="ReleaseFromRemittance"/> if the remittance is cancelled before sending.
    /// Settlement (→ Paid) happens only when a Payment is registered on return confirmation (SP-3b-2).</summary>
    public void MarkInRemittance()
    {
        if (Status != InstallmentStatus.Open)
            throw new InvalidOperationException($"Only an open installment can enter a remittance (was {Status}).");
        Status = InstallmentStatus.InRemittance;
    }

    public void ReleaseFromRemittance()
    {
        if (Status != InstallmentStatus.InRemittance)
            throw new InvalidOperationException($"Only an in-remittance installment can be released (was {Status}).");
        Status = InstallmentStatus.Open;
    }

    private void Recompute()
    {
        PaidAmount = _payments.Where(p => !p.IsReversed).Sum(p => p.PrincipalApplied);
        Status = PaidAmount <= 0 ? InstallmentStatus.Open
            : PaidAmount >= NetAmount ? InstallmentStatus.Paid
            : InstallmentStatus.PartiallyPaid;
    }
}
