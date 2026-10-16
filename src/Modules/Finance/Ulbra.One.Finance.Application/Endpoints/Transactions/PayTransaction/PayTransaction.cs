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
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Transactions.PayTransaction;

public sealed class PayTransaction : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id}/pay", Handle)
            .WithName("PayTransaction")
            .WithSummary("Settles a legacy transaction.")
            .WithDescription("LEGACY, superseded by the Payable aggregate. Unlike a payable, it settles the whole record "
                + "at once - there are no installments and no partial payment here.")
            .WithTags("Finance")
            .RequirePermission(Permissions.TreasuryWrite)
            .Produces<TransactionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> Handle(
        TransactionId id,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var t = await uow.Set<Transaction>().FirstOrDefaultAsync(x => x.Id == id.Value, ct);
        if (t is null) return TypedResults.NotFound();
        try { t.MarkAsPaid(); }
        catch (InvalidOperationException ex) { return ErrorResults.InvalidState(ex.Message, ErrorCodes.Transaction.InvalidState); }
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(TransactionResponse.From(t));
    }
}
