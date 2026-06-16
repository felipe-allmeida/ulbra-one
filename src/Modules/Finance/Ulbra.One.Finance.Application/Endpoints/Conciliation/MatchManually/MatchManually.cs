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
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Conciliation.MatchManually;

public sealed class MatchManually : IEndpoint
{
    public sealed record Request(InstallmentId InstallmentId);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/match", Handle).WithName("MatchManually").WithTags("Finance")
            .RequireAuthorization()
            .Produces<BankTransactionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(BankTransactionId id, Request req, UserContext user,
        [FromKeyedServices("finance")] IUnitOfWork uow, TimeProvider clock, CancellationToken ct)
    {
        var txn = await uow.Set<BankTransaction>().FirstOrDefaultAsync(t => t.Id == id.Value, ct);
        if (txn is null)
            return Results.Problem(detail: "Bank transaction not found.", statusCode: 404, title: "BankTransaction.NotFound");
        if (txn.MatchStatus is MatchStatus.Reconciled or MatchStatus.Ignored)
            return Results.Problem(detail: "The transaction is already reconciled or ignored.", statusCode: 409, title: "BankTransaction.AlreadyReconciled");

        return await ConciliationBaixa.ReconcileAsync(txn, req.InstallmentId.Value, user.UserId, uow, clock, ct);
    }
}
