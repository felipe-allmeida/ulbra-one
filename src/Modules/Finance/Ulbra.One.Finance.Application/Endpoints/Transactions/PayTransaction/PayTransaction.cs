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

namespace Ulbra.One.Finance.Application.Endpoints.Transactions.PayTransaction;

public sealed class PayTransaction : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id}/pay", Handle)
            .WithName("PayTransaction")
            .WithTags("Finance")
            .RequireAuthorization()
            .Produces<TransactionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> Handle(
        TransactionId id,
        [FromKeyedServices("finance")] IUnitOfWork uow,
        CancellationToken ct)
    {
        var t = await uow.Set<Transaction>().FirstOrDefaultAsync(x => x.Id == id.Value, ct);
        if (t is null) return Results.NotFound();
        try { t.MarkAsPaid(); }
        catch (InvalidOperationException ex) { return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict); }
        await uow.SaveChangesAsync(ct);
        return Results.Ok(TransactionResponse.From(t));
    }
}
