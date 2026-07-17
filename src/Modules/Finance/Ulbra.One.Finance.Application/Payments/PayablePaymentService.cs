using MassTransit;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain;
using Ulbra.One.Finance.Application.Endpoints.Payables;
using Ulbra.One.Finance.Application.Messaging;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

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
            return Result.Failure<Payable>(Error.Failure("Payment.AmountInvalid", "Amount must be greater than zero."));

        if (!payable.IsPayable)
            return Result.Failure<Payable>(Error.Failure("Payable.NotPayable", "Only an approved payable can be paid."));

        if (await ClassificationGate.RequiresButMissingAsync(_uow, payable, forPay: true, ct))
            return Result.Failure<Payable>(Error.Failure("Payable.NotClassified", "This payable must be classified before it can be paid."));

        if (input.Interest < 0 || input.Fine < 0 || input.Discount < 0)
            return Result.Failure<Payable>(Error.Failure("Payment.AmountInvalid", "Interest, fine and discount cannot be negative."));

        var principal = input.Amount - input.Interest - input.Fine + input.Discount;
        if (principal < 0)
            return Result.Failure<Payable>(Error.Failure("Payment.PrincipalNegative",
                "Payment principal cannot be negative (interest and fine exceed amount plus discount)."));

        var remaining = installment.NetAmount - installment.PaidAmount;
        if (principal > remaining + 0.005m)
            return Result.Failure<Payable>(Error.Failure("Payment.Overpay",
                "Payment principal exceeds the installment's outstanding balance."));

        // Project existence AND the bank's ledger-account link in one read (a plain Guid? projection would
        // conflate "bank not found" with "found but unmapped").
        var bank = await _uow.ReadSet<BankAccount>()
            .Where(b => b.Id == input.BankAccountId && b.CompanyId == payable.CompanyId)
            .Select(b => new { b.LedgerAccountId })
            .FirstOrDefaultAsync(ct);
        if (bank is null)
            return Result.Failure<Payable>(Error.Failure("Payment.BankAccountInvalid",
                "Bank account is invalid for this company."));

        var payment = new Payment(installment.Id, payable.Id, input.PaymentDate, input.Amount,
            input.BankAccountId, input.Method, input.Interest, input.Fine, input.Discount,
            input.Notes, createdById, _clock.GetUtcNow());

        try { installment.RegisterPayment(payment); }
        catch (InvalidOperationException ex)
            when (ex.Message.Contains("settled") || ex.Message.Contains("Installment is already"))
        {
            // Treated as a conflict — caller handles 409 for this specific case via the endpoint check.
            return Result.Failure<Payable>(Error.Conflict("Payment.InstallmentSettled", ex.Message));
        }

        payable.RecomputeSettlement();
        await _uow.AddAsync(payment, ct);

        await _publish.Publish(new PaymentRegistered(payment.Id, payable.Id, payable.CompanyId, payment.Amount, payment.PaymentDate, bank.LedgerAccountId, payable.DocumentNumber), ct);

        return Result.Success(payable);
    }
}
