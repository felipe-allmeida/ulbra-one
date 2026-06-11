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

namespace Ulbra.One.Finance.Application.Endpoints.Conciliation.ConfirmMatch;

public sealed class ConfirmMatch : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/confirm", Handle).WithName("ConfirmMatch").WithTags("Finance")
            .RequireAuthorization()
            .Produces<BankTransactionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(BankTransactionId id, UserContext user,
        [FromKeyedServices("finance")] IUnitOfWork uow, TimeProvider clock, CancellationToken ct)
    {
        var txn = await uow.Set<BankTransaction>().FirstOrDefaultAsync(t => t.Id == id.Value, ct);
        if (txn is null)
            return Results.Problem(detail: "Bank transaction not found.", statusCode: 404, title: "BankTransaction.NotFound");
        if (txn.MatchStatus != MatchStatus.Suggested)
            return Results.Problem(detail: "Only a suggested transaction can be confirmed.", statusCode: 409, title: "BankTransaction.NotSuggested");

        return await ConciliationBaixa.ReconcileAsync(txn, txn.SuggestedInstallmentId!.Value, user.UserId, uow, clock, ct);
    }
}
