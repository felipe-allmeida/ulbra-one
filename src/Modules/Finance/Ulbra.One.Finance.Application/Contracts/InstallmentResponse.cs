using Ulbra.One.Common.Application.Ids;
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
    int? RemittanceSequence);

public sealed record InstallmentResponse(
    InstallmentId Id, int Sequence, DateOnly DueDate, decimal Amount, InstallmentStatus Status, decimal PaidAmount,
    IReadOnlyList<PaymentResponse> Payments, decimal NetAmount,
    DateOnly ScheduledPaymentDate, bool IsHeld, string? HoldReason,
    string Rail, string? ManualReason,
    RemittanceId? RemittanceId, int? RemittanceSequence)
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
            var reason = RemittanceEligibility.Evaluate(
                ctx.DefaultPayingAccount, ctx.PayablePaymentMethod, ctx.VendorBankInfo);

            rail = reason == RemittanceManualReason.None ? "Remittance" : "Manual";
            manualReason = reason == RemittanceManualReason.None ? null : reason.ToString();
        }

        return new InstallmentResponse(
            new InstallmentId(i.Id), i.Sequence, i.DueDate, i.Amount, i.Status, i.PaidAmount,
            i.Payments.OrderBy(p => p.CreatedAt).Select(PaymentResponse.From).ToList(),
            i.NetAmount,
            i.ScheduledPaymentDate, i.IsHeld, i.HoldReason,
            rail, manualReason,
            ctx?.RemittanceId, ctx?.RemittanceSequence);
    }
}
