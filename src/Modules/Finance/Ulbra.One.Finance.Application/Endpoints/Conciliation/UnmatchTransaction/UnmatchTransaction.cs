using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Conciliation.UnmatchTransaction;

public sealed class UnmatchTransaction : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/unmatch", Handle).WithName("UnmatchTransaction").WithTags("Finance")
            .RequireAuthorization()
            .Produces<BankTransactionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(BankTransactionId id,
        [FromKeyedServices("finance")] IUnitOfWork uow, CancellationToken ct)
    {
        var txn = await uow.Set<BankTransaction>().FirstOrDefaultAsync(t => t.Id == id.Value, ct);
        if (txn is null)
            return Results.Problem(detail: "Bank transaction not found.", statusCode: 404, title: "BankTransaction.NotFound");
        if (txn.MatchStatus != MatchStatus.Reconciled)
            return Results.Problem(detail: "Only a reconciled transaction can be unmatched.", statusCode: 409, title: "BankTransaction.NotReconciled");

        var payable = await uow.Set<Payable>()
            .Include(p => p.Installments).ThenInclude(i => i.Payments)
            .FirstOrDefaultAsync(p => p.Installments.Any(i => i.Payments.Any(pay => pay.Id == txn.MatchedPaymentId)), ct);
        if (payable is null)
            return Results.Problem(detail: "Bank transaction not found.", statusCode: 404, title: "BankTransaction.NotFound");

        var payment = payable.FindPayment(txn.MatchedPaymentId!.Value)!;
        var installment = payable.FindInstallment(payment.InstallmentId)!;
        installment.ReversePayment(txn.MatchedPaymentId.Value, "Conciliação desfeita");
        payable.RecomputeSettlement();
        txn.Unreconcile();
        await uow.SaveChangesAsync(ct);

        return TypedResults.Ok(BankTransactionResponse.From(txn));
    }
}
