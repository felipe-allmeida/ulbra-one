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

namespace Ulbra.One.Finance.Application.Endpoints.Receivables.GetReceivable;

public sealed class GetReceivable : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/{id}", Handle).WithName("GetReceivable").WithTags("Finance")
            .RequireAuthorization().Produces<ReceivableResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(ReceivableId id, [FromKeyedServices("finance")] IUnitOfWork uow,
        TimeProvider clock, CancellationToken ct)
    {
        // Installments are not auto-included on Receivable (scale); include them explicitly here.
        // Receipts auto-include once installments are loaded.
        var r = await uow.ReadSet<Receivable>().Include(x => x.Installments).FirstOrDefaultAsync(x => x.Id == id.Value, ct);
        if (r is null) return Results.Problem(detail: "Receivable not found.", statusCode: 404, title: "Receivable.NotFound");
        return TypedResults.Ok(ReceivableResponse.From(r, ReceivableClock.Today(clock)));
    }
}
