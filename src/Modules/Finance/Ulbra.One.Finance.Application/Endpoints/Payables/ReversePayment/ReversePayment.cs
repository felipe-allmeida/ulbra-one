using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.ReversePayment;

public sealed class ReversePayment : IEndpoint
{
    public sealed record Request(string? Reason);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/payments/{paymentId}/reverse", Handle)
            .WithName("ReversePayment").WithTags("Finance").RequireAuthorization()
            .Produces<PayableResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(PayableId id, PaymentId paymentId, Request req,
        IUnitOfWork uow, CancellationToken ct)
    {
        var payable = await uow.Set<Payable>()
            .Include(p => p.Installments).ThenInclude(i => i.Payments)
            .FirstOrDefaultAsync(p => p.Id == id.Value, ct);
        if (payable is null) return TypedResults.Problem(detail: "Payable not found.", statusCode: 404, title: "Payable.NotFound");

        var payment = payable.FindPayment(paymentId.Value);
        if (payment is null) return TypedResults.Problem(detail: "Payment not found.", statusCode: 404, title: "Payment.NotFound");

        var installment = payable.FindInstallment(payment.InstallmentId);
        try { installment!.ReversePayment(paymentId.Value, req.Reason); }
        catch (InvalidOperationException ex) { return TypedResults.Problem(detail: ex.Message, statusCode: 409, title: "Payment.AlreadyReversed"); }
        payable.RecomputeSettlement();

        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(PayableResponse.From(payable));
    }
}
