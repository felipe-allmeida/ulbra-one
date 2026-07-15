using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Conciliation.IgnoreTransaction;

public sealed class IgnoreTransaction : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/ignore", Handle).WithName("IgnoreTransaction").WithTags("Finance")
            .RequirePermission(Permissions.TreasuryWrite)
            .Produces<BankTransactionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(BankTransactionId id,
        IUnitOfWork uow, CancellationToken ct)
    {
        var txn = await uow.Set<BankTransaction>().FirstOrDefaultAsync(t => t.Id == id.Value, ct);
        if (txn is null)
            return TypedResults.Problem(detail: "Bank transaction not found.", statusCode: 404, title: "BankTransaction.NotFound");
        if (txn.MatchStatus == MatchStatus.Reconciled)
            return TypedResults.Problem(detail: "Unmatch the transaction before ignoring it.", statusCode: 409, title: "BankTransaction.AlreadyReconciled");

        txn.Ignore();
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(BankTransactionResponse.From(txn));
    }
}
