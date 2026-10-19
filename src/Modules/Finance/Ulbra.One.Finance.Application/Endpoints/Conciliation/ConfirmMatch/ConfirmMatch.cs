using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

namespace Ulbra.One.Finance.Application.Endpoints.Conciliation.ConfirmMatch;

public sealed class ConfirmMatch : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/confirm", Handle).WithName("ConfirmMatch").WithTags("Finance")
            .WithSummary("Confirms a suggested match.")
            .WithDescription("Accepts the installment the system suggested for a statement line. Like a manual match, "
                + "confirming REGISTERS THE PAYMENT against that installment - it is the moment the settlement "
                + "becomes real. A line with no suggestion returns 400.")
            .RequirePermission(Permissions.TreasuryWrite)
            .Produces<BankTransactionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(BankTransactionId id, UserContext user,
        IUnitOfWork uow, IPayablePaymentService paymentService, CancellationToken ct)
    {
        var txn = await uow.Set<BankTransaction>().FirstOrDefaultAsync(t => t.Id == id.Value, ct);
        if (txn is null)
            return ErrorResults.NotFound("Bank transaction not found.", ErrorCodes.BankTransaction.NotFound);
        if (txn.MatchStatus != MatchStatus.Suggested)
            return ErrorResults.InvalidState("Only a suggested transaction can be confirmed.", ErrorCodes.BankTransaction.NotSuggested);

        return await ConciliationBaixa.ReconcileAsync(txn, txn.SuggestedInstallmentId!.Value, user.UserId, uow, paymentService, ct);
    }
}
