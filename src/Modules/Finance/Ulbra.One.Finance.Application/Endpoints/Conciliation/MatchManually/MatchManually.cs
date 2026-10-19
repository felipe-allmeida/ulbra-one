using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.Payments;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Conciliation.MatchManually;

public sealed class MatchManually : IEndpoint
{
    public sealed record Request(
        [property: SwaggerSchema("Installment this bank transaction settles. Registering the payment "
            + "against it is what the match does.")]
        InstallmentId InstallmentId);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/match", Handle).WithName("MatchManually").WithTags("Finance")
            .WithSummary("Matches a bank transaction to an installment.")
            .WithDescription("Points a statement line at the installment it settles, when the automatic suggestion got it "
                + "wrong or found nothing. This is what REGISTERS THE PAYMENT: matching is not a note, it "
                + "settles the installment and moves money in the books, exactly as registering a payment by "
                + "hand would.")
            .RequirePermission(Permissions.TreasuryWrite)
            .Produces<BankTransactionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(BankTransactionId id, Request req, UserContext user,
        IUnitOfWork uow, IPayablePaymentService paymentService, CancellationToken ct)
    {
        var txn = await uow.Set<BankTransaction>().FirstOrDefaultAsync(t => t.Id == id.Value, ct);
        if (txn is null)
            return ErrorResults.NotFound("Bank transaction not found.", ErrorCodes.BankTransaction.NotFound);
        if (txn.MatchStatus is MatchStatus.Reconciled or MatchStatus.Ignored)
            return ErrorResults.InvalidState("The transaction is already reconciled or ignored.", ErrorCodes.BankTransaction.AlreadyReconciled);

        return await ConciliationBaixa.ReconcileAsync(txn, req.InstallmentId.Value, user.UserId, uow, paymentService, ct);
    }
}
