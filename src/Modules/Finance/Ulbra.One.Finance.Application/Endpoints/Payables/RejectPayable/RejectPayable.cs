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

namespace Ulbra.One.Finance.Application.Endpoints.Payables.RejectPayable;

public sealed class RejectPayable : IEndpoint
{
    public sealed record RejectRequest(string? Reason);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/reject", Handle).WithName("RejectPayable").WithTags("Finance")
            .RequireAuthorization().Produces<PayableResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(PayableId id, RejectRequest req,
        [FromKeyedServices("finance")] IUnitOfWork uow, CancellationToken ct)
    {
        var p = await uow.Set<Payable>().FirstOrDefaultAsync(x => x.Id == id.Value, ct);
        if (p is null) return Results.Problem(detail: "Payable not found.", statusCode: 404, title: "Payable.NotFound");
        try { p.Reject(req.Reason); }
        catch (InvalidOperationException ex) { return Results.Problem(detail: ex.Message, statusCode: 409, title: "Payable.InvalidState"); }
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(PayableResponse.From(p));
    }
}
