using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Finance.Application.Remittances;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Registry.Domain.ValueObjects;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>Context required to compute the remittance rail fields for an installment.</summary>
public sealed record InstallmentRailContext(
    BankAccount? DefaultPayingAccount,
    BankInfo VendorBankInfo,
    PaymentMethod? PayablePaymentMethod,
    RemittanceId? RemittanceId,
    int? RemittanceSequence,
    bool EnablePixQrCode = true);

[SwaggerSchema("One installment of a payable — the unit that actually gets paid. It also carries how it "
    + "would be paid: by CNAB remittance or by hand, and if by hand, why.")]
public sealed record InstallmentResponse(
    InstallmentId Id,
    [property: SwaggerSchema("Position in the payable's plan, starting at 1.")]
    [property: SwaggerExample("1")]
    int Sequence,
    [property: SwaggerSchema("When the installment falls due.")]
    [property: SwaggerExample("2026-08-10")]
    DateOnly DueDate,
    [property: SwaggerSchema("Face value of the installment, in BRL, before withholdings.")]
    [property: SwaggerExample("1500.00")]
    decimal Amount,
    [property: SwaggerSchema("Where the installment stands.")]
    InstallmentStatus Status,
    [property: SwaggerSchema("Paid so far, in BRL — the sum of non-reversed payments. Partial payment is "
        + "allowed, so this can sit between zero and NetAmount.")]
    [property: SwaggerExample("1430.25")]
    decimal PaidAmount,
    [property: SwaggerSchema("Payments made against this installment, oldest first. A reversed payment "
        + "stays in the list; it just stops counting toward PaidAmount.")]
    IReadOnlyList<PaymentResponse> Payments,
    [property: SwaggerSchema("What settles the installment, in BRL: Amount minus its share of the "
        + "withholdings. This — not Amount — is what a payment is measured against.")]
    [property: SwaggerExample("1430.25")]
    decimal NetAmount,
    [property: SwaggerSchema("When the treasury intends to pay it. Distinct from DueDate, which is what "
        + "was agreed with the vendor.")]
    [property: SwaggerExample("2026-08-08")]
    DateOnly ScheduledPaymentDate,
    [property: SwaggerSchema("Whether the installment is held back from the payment queue.")]
    bool IsHeld,
    [property: SwaggerSchema("Why it is held. Present only while IsHeld is true.")]
    [property: SwaggerExample("Aguardando retificação da nota fiscal.")]
    string? HoldReason,
    [property: SwaggerSchema("How this installment is to be paid, when it differs from the payable's "
        + "method.")]
    PaymentMethod? PaymentMethod,
    [property: SwaggerSchema("Boleto linha digitável, 47 or 48 digits. Null unless paid by boleto.")]
    [property: SwaggerExample("34191790010104351004791020150008291070026000")]
    string? BoletoDigitableLine,
    [property: SwaggerSchema("Per-payment PIX key override (AEL-938). Null unless paid by PIX with an override "
        + "— when set, the CNAB remittance uses it instead of the vendor's registered PIX key for this payment.")]
    [property: SwaggerExample("pagamentos@fornecedor.com.br")]
    string? PixKeyOverride,
    [property: SwaggerSchema("Per-payment PIX \"copia e cola\" (EMV/BR Code, AEL-963). Null unless paid by a "
        + "PIX QR — a static QR decodes to a key, a dynamic QR rides the CNAB forma 47 remittance.")]
    string? PixPayloadEmv,
    [property: SwaggerSchema("How the installment can be paid: `Remittance` when it is eligible for a "
        + "CNAB file, `Manual` otherwise. Computed on read from the paying account, the method and the "
        + "vendor's bank data — so it changes when any of those do.")]
    [property: SwaggerExample("Remittance")]
    string Rail,
    [property: SwaggerSchema("Why the installment cannot be remitted, when Rail is `Manual` — an "
        + "unsupported bank, a missing agreement code, incomplete payer data. Null when it is eligible. "
        + "This is the field that explains a disabled checkbox in the payment queue.")]
    [property: SwaggerExample("BankNotSupported")]
    string? ManualReason,
    [property: SwaggerSchema("The remittance this installment travelled in, when it is in one. While that "
        + "remittance is pending the installment cannot be paid by hand.")]
    RemittanceId? RemittanceId,
    [property: SwaggerSchema("Position of this installment inside that remittance file.")]
    [property: SwaggerExample("42")]
    int? RemittanceSequence)
{
    /// <summary>Basic factory — no rail/hold enrichment. Used internally when the full context is not available
    /// (e.g. from <see cref="PayableResponse"/> built without the extra query).</summary>
    public static InstallmentResponse From(Installment i) =>
        From(i, null);

    /// <summary>Full factory — computes rail/manualReason/remittance fields from the provided context.</summary>
    public static InstallmentResponse From(Installment i, InstallmentRailContext? ctx)
    {
        string rail;
        string? manualReason;

        if (ctx is null)
        {
            // No context: treat as Manual (we cannot evaluate eligibility without the paying account).
            rail = "Manual";
            manualReason = null;
        }
        else if (ctx.DefaultPayingAccount is null)
        {
            // No default paying account configured → cannot remit.
            rail = "Manual";
            manualReason = RemittanceManualReason.BankNotSupported.ToString();
        }
        else
        {
            // Effective method: the installment's own method wins, else the payable's (AEL-933).
            var reason = RemittanceEligibility.Evaluate(
                ctx.DefaultPayingAccount, i.PaymentMethod ?? ctx.PayablePaymentMethod, ctx.VendorBankInfo, i.BoletoDigitableLine, i.PixKeyOverride, i.PixPayloadEmv, ctx.EnablePixQrCode);

            rail = reason == RemittanceManualReason.None ? "Remittance" : "Manual";
            manualReason = reason == RemittanceManualReason.None ? null : reason.ToString();
        }

        return new InstallmentResponse(
            new InstallmentId(i.Id), i.Sequence, i.DueDate, i.Amount, i.Status, i.PaidAmount,
            i.Payments.OrderBy(p => p.CreatedAt).Select(PaymentResponse.From).ToList(),
            i.NetAmount,
            i.ScheduledPaymentDate, i.IsHeld, i.HoldReason,
            i.PaymentMethod,
            i.BoletoDigitableLine,
            i.PixKeyOverride,
            i.PixPayloadEmv,
            rail, manualReason,
            ctx?.RemittanceId, ctx?.RemittanceSequence);
    }
}
