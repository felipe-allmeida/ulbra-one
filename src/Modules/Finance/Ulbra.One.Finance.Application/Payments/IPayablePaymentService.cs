using Ulbra.One.Common.Domain;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Payments;

/// <summary>Input data for registering a baixa against an installment.</summary>
public record RegisterPaymentInput(
    DateOnly PaymentDate,
    decimal Amount,
    Guid BankAccountId,
    PaymentMethod Method,
    decimal Interest,
    decimal Fine,
    decimal Discount,
    string? Notes);

/// <summary>
/// Shared validation + registration path for all baixa writers (manual endpoint and auto-pay job).
/// Does NOT call <c>SaveChangesAsync</c> — the caller owns the unit-of-work boundary.
/// </summary>
public interface IPayablePaymentService
{
    /// <summary>
    /// Validates and registers a payment against the given <paramref name="installment"/>.
    /// On success, <paramref name="installment"/> and <paramref name="payable"/> are mutated in-memory
    /// (the caller must persist via <c>SaveChangesAsync</c>).
    /// </summary>
    Task<Result<Payable>> RegisterAsync(
        Payable payable,
        Installment installment,
        RegisterPaymentInput input,
        Guid createdById,
        CancellationToken ct);
}
