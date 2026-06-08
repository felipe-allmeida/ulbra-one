using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Infrastructure.Authentication;
using Ulbra.One.Finance.Application.Contracts;
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
        UserContext user, [FromKeyedServices("finance")] IUnitOfWork uow, TimeProvider clock, CancellationToken ct)
    {
        if (req.Amount <= 0)
            return Results.Problem(detail: "Amount must be greater than zero.", statusCode: 400, title: "Payment.AmountInvalid");

        var payable = await uow.Set<Payable>()
            .Include(p => p.Installments).ThenInclude(i => i.Payments)
            .FirstOrDefaultAsync(p => p.Id == id.Value, ct);
        if (payable is null) return Results.Problem(detail: "Payable not found.", statusCode: 404, title: "Payable.NotFound");
        if (!payable.IsPayable) return Results.Problem(detail: "Only an approved payable can be paid.", statusCode: 400, title: "Payable.NotPayable");

        var installment = payable.FindInstallment(installmentId.Value);
        if (installment is null) return Results.Problem(detail: "Installment not found.", statusCode: 404, title: "Payable.InstallmentNotFound");

        var bankOk = await uow.ReadSet<BankAccount>().AnyAsync(b => b.Id == req.BankAccountId.Value && b.CompanyId == payable.CompanyId, ct);
        if (!bankOk) return Results.Problem(detail: "Bank account is invalid for this company.", statusCode: 400, title: "Payment.BankAccountInvalid");

        var payment = new Payment(installment.Id, payable.Id, req.PaymentDate, req.Amount, req.BankAccountId.Value,
            req.Method, req.Interest, req.Fine, req.Discount, req.Notes, Guid.Parse(user.UserId), clock.GetUtcNow());

        try { installment.RegisterPayment(payment); }
        catch (InvalidOperationException ex) { return Results.Problem(detail: ex.Message, statusCode: 409, title: "Payment.InstallmentSettled"); }
        payable.RecomputeSettlement();

        await uow.AddAsync(payment, ct); // explicit: grandchild not auto-inserted on aggregate update
        await uow.SaveChangesAsync(ct);
        return Results.Created($"/api/payables/{id}", PayableResponse.From(payable));
    }
}
