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

namespace Ulbra.One.Finance.Application.Endpoints.Receivables.CancelReceivable;

public sealed class CancelReceivable : IEndpoint
{
    public sealed record CancelReceivableRequest(string? Reason);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/cancel", Handle).WithName("CancelReceivable").WithTags("Finance")
            .RequireAuthorization().Produces<ReceivableResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(ReceivableId id, CancelReceivableRequest req,
        [FromKeyedServices("finance")] IUnitOfWork uow, TimeProvider clock, CancellationToken ct)
    {
        var r = await uow.Set<Receivable>().Include(x => x.Installments).FirstOrDefaultAsync(x => x.Id == id.Value, ct);
        if (r is null) return Results.Problem(detail: "Receivable not found.", statusCode: 404, title: "Receivable.NotFound");
        try { r.Cancel(req.Reason); }
        catch (InvalidOperationException ex) { return Results.Problem(detail: ex.Message, statusCode: 409, title: "Receivable.InvalidState"); }
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(ReceivableResponse.From(r, ReceivableClock.Today(clock)));
    }
}
