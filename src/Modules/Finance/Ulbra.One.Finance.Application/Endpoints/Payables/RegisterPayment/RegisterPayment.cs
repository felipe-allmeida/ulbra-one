using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.Payments;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.RegisterPayment;

public sealed class RegisterPayment : IEndpoint
{
    public sealed record Request(
        DateOnly PaymentDate, decimal Amount, BankAccountId BankAccountId, PaymentMethod Method,
        decimal Interest = 0, decimal Fine = 0, decimal Discount = 0, string? Notes = null);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/installments/{installmentId}/payments", Handle)
            .WithName("RegisterPayment").WithTags("Finance").RequireAuthorization()
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
            return TypedResults.Problem(detail: "Payable not found.", statusCode: 404, title: "Payable.NotFound");

        var installment = payable.FindInstallment(installmentId.Value);
        if (installment is null)
            return TypedResults.Problem(detail: "Installment not found.", statusCode: 404, title: "Payable.InstallmentNotFound");
        if (installment.Status == InstallmentStatus.Paid)
            return TypedResults.Problem(detail: "Installment is already settled.", statusCode: 409, title: "Payment.InstallmentSettled");

        var input = new RegisterPaymentInput(req.PaymentDate, req.Amount, req.BankAccountId.Value,
            req.Method, req.Interest, req.Fine, req.Discount, req.Notes);

        var result = await paymentService.RegisterAsync(payable, installment, input, Guid.Parse(user.UserId), ct);

        if (result.IsFailure)
        {
            return result.Error.Code switch
            {
                "Payment.AmountInvalid" => TypedResults.Problem(detail: result.Error.Description, statusCode: 400, title: result.Error.Code),
                "Payable.NotPayable" => TypedResults.Problem(detail: result.Error.Description, statusCode: 400, title: result.Error.Code),
                "Payable.NotClassified" => TypedResults.Problem(detail: result.Error.Description, statusCode: 400, title: result.Error.Code),
                "Payment.PrincipalNegative" => TypedResults.Problem(detail: result.Error.Description, statusCode: 400, title: result.Error.Code),
                "Payment.Overpay" => TypedResults.Problem(detail: result.Error.Description, statusCode: 400, title: result.Error.Code),
                "Payment.BankAccountInvalid" => TypedResults.Problem(detail: result.Error.Description, statusCode: 400, title: result.Error.Code),
                "Payment.InstallmentSettled" => TypedResults.Problem(detail: result.Error.Description, statusCode: 409, title: result.Error.Code),
                _ => TypedResults.Problem(detail: result.Error.Description, statusCode: 400, title: result.Error.Code),
            };
        }

        await uow.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/payables/{id}", PayableResponse.From(payable));
    }
}
