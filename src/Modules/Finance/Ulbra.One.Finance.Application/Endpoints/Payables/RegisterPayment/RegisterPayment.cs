using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.Payments;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.RegisterPayment;

public sealed class RegisterPayment : IEndpoint
{
    public sealed record Request(
        [property: SwaggerSchema("When the money actually left the account (data de pagamento).")]
        [property: SwaggerExample("2026-09-10")]
        DateOnly PaymentDate,
        [property: SwaggerSchema("Principal settled, in BRL — the part that reduces the installment "
            + "balance. Interest, fine and discount are recorded apart and do NOT count toward it. "
            + "Paying beyond the outstanding balance is rejected.")]
        [property: SwaggerExample("1250.00")]
        decimal Amount,
        [property: SwaggerSchema("Bank account the money came out of.")]
        BankAccountId BankAccountId,
        [property: SwaggerSchema("How it was paid.")]
        PaymentMethod Method,
        [property: SwaggerSchema("Interest paid on top of the principal, in BRL. Non-negative.")]
        [property: SwaggerExample("0.00")]
        decimal Interest = 0,
        [property: SwaggerSchema("Late fee paid on top of the principal, in BRL. Non-negative.")]
        [property: SwaggerExample("0.00")]
        decimal Fine = 0,
        [property: SwaggerSchema("Discount granted, in BRL. Non-negative.")]
        [property: SwaggerExample("0.00")]
        decimal Discount = 0,
        [property: SwaggerSchema("Free-text note kept on the payment record.")]
        string? Notes = null);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/installments/{installmentId}/payments", Handle)
            .WithName("RegisterPayment").WithTags("Finance")
            .WithSummary("Registers a payment against an installment.")
            .WithDescription("Settles part or all of one installment. Payments are APPEND-ONLY: once a "
                + "payable has one, it can no longer be edited or cancelled, and undoing a payment means "
                + "reversing it rather than deleting it. Amount is the principal and cannot exceed the "
                + "outstanding balance; interest, fine and discount are recorded separately and do not "
                + "count toward it. Partial payments are fine — the installment settles when the balance "
                + "reaches zero.").RequirePermission(Permissions.PayablesPay)
            .Produces<PayableResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(PayableId id, InstallmentId installmentId, Request req,
        UserContext user, IUnitOfWork uow, IPayablePaymentService paymentService, CancellationToken ct)
    {
        var payable = await uow.Set<Payable>()
            .Include(p => p.Installments).ThenInclude(i => i.Payments)
            .FirstOrDefaultAsync(p => p.Id == id.Value, ct);
        if (payable is null)
            return ErrorResults.NotFound("Payable not found.", ErrorCodes.Payable.NotFound);

        var installment = payable.FindInstallment(installmentId.Value);
        if (installment is null)
            return ErrorResults.NotFound("Installment not found.", ErrorCodes.Payable.InstallmentNotFound);
        if (installment.Status == InstallmentStatus.InRemittance)
            return ErrorResults.InvalidState("Esta parcela está em uma remessa pendente.", ErrorCodes.Payment.InstallmentInRemittance);
        if (installment.Status == InstallmentStatus.Paid)
            return ErrorResults.InvalidState("Installment is already settled.", ErrorCodes.Payment.InstallmentSettled);

        var input = new RegisterPaymentInput(req.PaymentDate, req.Amount, req.BankAccountId.Value,
            req.Method, req.Interest, req.Fine, req.Discount, req.Notes);

        var result = await paymentService.RegisterAsync(payable, installment, input, Guid.Parse(user.UserId), ct);

        if (result.IsFailure)
        {
            return result.Error.Code switch
            {
                ErrorCodes.Payment.AmountInvalid => ErrorResults.InvalidRequest(result.Error.Description, result.Error.Code),
                ErrorCodes.Payable.NotPayable => ErrorResults.InvalidRequest(result.Error.Description, result.Error.Code),
                ErrorCodes.Payable.NotClassified => ErrorResults.InvalidRequest(result.Error.Description, result.Error.Code),
                ErrorCodes.Payment.PrincipalNegative => ErrorResults.InvalidRequest(result.Error.Description, result.Error.Code),
                ErrorCodes.Payment.Overpay => ErrorResults.InvalidRequest(result.Error.Description, result.Error.Code),
                ErrorCodes.Payment.BankAccountInvalid => ErrorResults.InvalidRequest(result.Error.Description, result.Error.Code),
                ErrorCodes.Payment.InstallmentSettled => ErrorResults.InvalidState(result.Error.Description, result.Error.Code),
                _ => ErrorResults.InvalidRequest(result.Error.Description, result.Error.Code),
            };
        }

        await uow.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/payables/{id}", PayableResponse.From(payable));
    }
}
