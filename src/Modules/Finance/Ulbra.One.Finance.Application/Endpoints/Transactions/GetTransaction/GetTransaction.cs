using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Transactions.GetTransaction;

public sealed class GetTransaction : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("/{id:guid}", Handle)
            .WithName("GetTransaction")
            .WithTags("Finance")
            .RequireAuthorization()
            .Produces<TransactionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        Guid id,
        [FromKeyedServices("finance")] IUnitOfWork uow,
        CancellationToken ct)
        => await uow.ReadSet<Transaction>().FirstOrDefaultAsync(l => l.Id == id, ct) is { } l
            ? Results.Ok(TransactionResponse.From(l))
            : Results.NotFound();
}
