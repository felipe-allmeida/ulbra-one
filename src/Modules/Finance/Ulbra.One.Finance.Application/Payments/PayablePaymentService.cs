using MassTransit;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain;
using Ulbra.One.Finance.Application.Endpoints.Payables;
using Ulbra.One.Finance.Application.Messaging;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Finance.Domain.Withholdings;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Finance.Application.Payments;

/// <summary>
/// Shared baixa registration logic used by both the manual <c>RegisterPayment</c> endpoint
/// and the auto-pay background job. Does NOT call <c>SaveChangesAsync</c>.
/// </summary>
public sealed class PayablePaymentService : IPayablePaymentService
{
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;
    private readonly IPublishEndpoint _publish;

    public PayablePaymentService(IUnitOfWork uow, TimeProvider clock, IPublishEndpoint publish)
    {
        _uow = uow;
        _clock = clock;
        _publish = publish;
    }

    public async Task<Result<Payable>> RegisterAsync(
        Payable payable,
        Installment installment,
        RegisterPaymentInput input,
        Guid createdById,
        CancellationToken ct)
    {
        if (input.Amount <= 0)
            return Result.Failure<Payable>(Error.Failure(ErrorCodes.Payment.AmountInvalid, "Amount must be greater than zero."));

        if (!payable.IsPayable)
            return Result.Failure<Payable>(Error.Failure(ErrorCodes.Payable.NotPayable, "Only an approved payable can be paid."));

        if (await ClassificationGate.RequiresButMissingAsync(_uow, payable, forPay: true, ct))
            return Result.Failure<Payable>(Error.Failure(ErrorCodes.Payable.NotClassified, "This payable must be classified before it can be paid."));

        if (input.Interest < 0 || input.Fine < 0 || input.Discount < 0)
            return Result.Failure<Payable>(Error.Failure(ErrorCodes.Payment.AmountInvalid, "Interest, fine and discount cannot be negative."));

        var principal = input.Amount - input.Interest - input.Fine + input.Discount;
        if (principal < 0)
            return Result.Failure<Payable>(Error.Failure(ErrorCodes.Payment.PrincipalNegative,
                "Payment principal cannot be negative (interest and fine exceed amount plus discount)."));

        var remaining = installment.NetAmount - installment.PaidAmount;
        if (principal > remaining + 0.005m)
            return Result.Failure<Payable>(Error.Failure(ErrorCodes.Payment.Overpay,
                "Payment principal exceeds the installment's outstanding balance."));

        // Project existence AND the bank's ledger-account link in one read (a plain Guid? projection would
        // conflate "bank not found" with "found but unmapped").
        var bank = await _uow.ReadSet<BankAccount>()
            .Where(b => b.Id == input.BankAccountId && b.CompanyId == payable.CompanyId)
            .Select(b => new { b.LedgerAccountId })
            .FirstOrDefaultAsync(ct);
        if (bank is null)
            return Result.Failure<Payable>(Error.Failure(ErrorCodes.Payment.BankAccountInvalid,
                "Bank account is invalid for this company."));

        var payment = new Payment(installment.Id, payable.Id, input.PaymentDate, input.Amount,
            input.BankAccountId, input.Method, input.Interest, input.Fine, input.Discount,
            input.Notes, createdById, _clock.GetUtcNow());

        try { installment.RegisterPayment(payment); }
        catch (InvalidOperationException ex)
            when (ex.Message.Contains("settled") || ex.Message.Contains("Installment is already"))
        {
            // Treated as a conflict — caller handles 409 for this specific case via the endpoint check.
            return Result.Failure<Payable>(Error.Conflict(ErrorCodes.Payment.InstallmentSettled, ex.Message));
        }

        payable.RecomputeSettlement();

        // Recognize the payment-time retentions (CSRF: PIS/COFINS/CSLL) prorated to this baixa. The document-level
        // CSRF total (PayableWithholding) is spread across payments by the net cash applied; the payment that
        // settles the payable absorbs the rounding residual so Σ per tax equals the document total exactly.
        var csrf = ComputePaymentWithholdings(payable, payment);
        payment.SetWithholdings(csrf);

        await _uow.AddAsync(payment, ct);

        var settled = csrf.Select(c => new SettledWithholding(c.TaxType.ToString(), c.Amount)).ToList();

        // Supplier identification for the Livro Razão history (razão social + CNPJ). Cross-module read by the
        // loose Guid VendorId; falls back to the payable's snapshot name when the vendor is missing.
        var vendor = await _uow.ReadSet<VendorEntity>()
            .Where(v => v.Id == payable.VendorId)
            .Select(v => new { v.Name, v.Document })
            .FirstOrDefaultAsync(ct);
        await _publish.Publish(new PaymentRegistered(payment.Id, payable.Id, payable.CompanyId, payment.Amount,
            payment.PaymentDate, bank.LedgerAccountId, settled, payable.DocumentNumber,
            CounterpartyName: vendor?.Name ?? payable.CounterpartyName,
            CounterpartyDocument: vendor?.Document,
            // Budgeting loopback: back-reference + the paid installment's competency ("YYYY-MM"), so the
            // real "realizado" lands on the same competency the estimate contributed to (null for non-
            // requisition payables — the Budgeting consumer no-ops).
            RequisitionId: payable.OriginRequisitionId,
            Competency: $"{installment.DueDate:yyyy-MM}",
            // Gross-equivalent realized by this baixa, so the requisition's realizado (compared against the
            // GROSS estimate) closes exactly even though the payable settles net of withholdings.
            GrossAmount: ComputeGrossApplied(payable, payment),
            // Estabelecimento (unit) so every line of the settlement entry is unit-tagged (per-unit balancete).
            UnitId: payable.OriginUnitId), ct);

        return Result.Success(payable);
    }

    /// <summary>Gross-equivalent of <see cref="Payable.TotalAmount"/> realized by <paramref name="current"/>:
    /// principal-based (interest/fine/discount excluded), grossed up by TotalAmount/NetTotal. Each non-settling
    /// baixa gets its proportional share (order-independent); the baixa that settles the payable absorbs the
    /// rounding residual (TotalAmount − Σ shares of the other non-reversed payments), so Σ over the life ==
    /// TotalAmount exactly. A rare reversal interleaving can drift ±0.01 (the residual is recomputed from the
    /// current non-reversed set); exact-in-all-cases would require persisting the gross per payment.</summary>
    private static decimal ComputeGrossApplied(Payable payable, Payment current)
    {
        var netTotal = payable.NetTotal;
        if (netTotal <= 0) return current.PrincipalApplied; // degenerate (no net cash) — avoid divide-by-zero

        decimal Share(Payment p) =>
            Math.Round(payable.TotalAmount * p.PrincipalApplied / netTotal, 2, MidpointRounding.AwayFromZero);

        // A payment against a Paid payable can only be the one that just settled it (the overpay guard rejects a
        // payment on an already-settled installment), so when Status is Paid this IS the settling baixa.
        if (payable.Status == PayableStatus.Paid)
        {
            var others = payable.Installments
                .SelectMany(i => i.Payments)
                .Where(p => !p.IsReversed && p.Id != current.Id)
                .Sum(Share);
            return payable.TotalAmount - others;
        }
        return Share(current);
    }

    /// <summary>Prorates the payable's payment-time retentions (CSRF) to <paramref name="current"/>, the just-
    /// registered payment. Running-sum method: this payment gets (ideal cumulative withheld through it) − (already
    /// withheld on prior non-reversed payments), so rounding never drifts and the settling payment lands exactly
    /// on the document total. Fraction basis is net cash applied (Σ PrincipalApplied == NetTotal over the life).</summary>
    private static List<(WithholdingTaxType TaxType, decimal BaseAmount, decimal Amount)> ComputePaymentWithholdings(Payable payable, Payment current)
    {
        var paymentTaxes = payable.Withholdings
            .Where(w => WithholdingTimings.For(w.TaxType) == WithholdingTiming.OnPayment)
            .ToList();
        if (paymentTaxes.Count == 0) return [];

        var netTotal = payable.NetTotal;
        if (netTotal <= 0) return [];

        var nonReversed = payable.Installments
            .SelectMany(i => i.Payments)
            .Where(p => !p.IsReversed)
            .ToList();
        var cumulativePrincipal = nonReversed.Sum(p => p.PrincipalApplied);
        var fullyPaid = payable.Status == PayableStatus.Paid;

        var result = new List<(WithholdingTaxType TaxType, decimal BaseAmount, decimal Amount)>(paymentTaxes.Count);
        foreach (var tax in paymentTaxes)
        {
            var priorForTax = nonReversed
                .Where(p => p.Id != current.Id)
                .SelectMany(p => p.Withholdings)
                .Where(w => w.TaxType == tax.TaxType)
                .ToList();
            var alreadyAmount = priorForTax.Sum(w => w.Amount);
            var alreadyBase = priorForTax.Sum(w => w.BaseAmount);
            var thisAmount = WithholdingProration.ForPayment(tax.Amount, cumulativePrincipal, netTotal, alreadyAmount, fullyPaid);
            var thisBase = WithholdingProration.ForPayment(tax.BaseAmount, cumulativePrincipal, netTotal, alreadyBase, fullyPaid);
            if (thisAmount > 0)
                result.Add((tax.TaxType, thisBase, thisAmount));
        }
        return result;
    }
}
