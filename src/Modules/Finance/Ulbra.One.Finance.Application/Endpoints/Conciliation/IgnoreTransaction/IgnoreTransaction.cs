using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Conciliation.IgnoreTransaction;

public sealed class IgnoreTransaction : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/ignore", Handle).WithName("IgnoreTransaction").WithTags("Finance")
            .WithSummary("Ignores a bank transaction.")
            .WithDescription("Takes a statement line out of the reconciliation queue without settling anything - for lines "
                + "that are not payables at all, like fees or transfers between own accounts. It is a queue "
                + "decision, not a financial one: nothing is posted.")
            .RequirePermission(Permissions.TreasuryWrite)
            .Produces<BankTransactionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(BankTransactionId id,
        IUnitOfWork uow, CancellationToken ct)
    {
        var txn = await uow.Set<BankTransaction>().FirstOrDefaultAsync(t => t.Id == id.Value, ct);
        if (txn is null)
            return ErrorResults.NotFound("Bank transaction not found.", ErrorCodes.BankTransaction.NotFound);
        if (txn.MatchStatus == MatchStatus.Reconciled)
            return ErrorResults.InvalidState("Unmatch the transaction before ignoring it.", ErrorCodes.BankTransaction.AlreadyReconciled);

        txn.Ignore();
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(BankTransactionResponse.From(txn));
    }
}
