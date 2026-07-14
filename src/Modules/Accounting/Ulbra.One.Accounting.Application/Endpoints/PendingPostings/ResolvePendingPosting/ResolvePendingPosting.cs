using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Accounting.Application.Contracts;
using Ulbra.One.Accounting.Application.Posting;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Accounting.Application.Endpoints.PendingPostings.ResolvePendingPosting;

/// <summary>Reprocesses a parked pending posting from its stored plan — used after the missing ledger
/// account is created/activated or the target period is reopened. Idempotent: an already-resolved pending
/// returns unchanged; still-unresolvable input yields 409.</summary>
public sealed class ResolvePendingPosting : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/resolve", Handle).WithName("ResolvePendingPosting").WithTags("Accounting")
            .RequireAuthorization()
            .Produces<PendingAccountingPostingResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(
        PendingAccountingPostingId id, IUnitOfWork uow, IPayablePostingService posting, CancellationToken ct)
    {
        var pending = await uow.Set<PendingAccountingPosting>().FirstOrDefaultAsync(p => p.Id == id.Value, ct);
        if (pending is null)
            return TypedResults.Problem(detail: "Pending posting not found.", statusCode: 404, title: "PendingPosting.NotFound");

        var resolved = await posting.ResolvePendingAsync(pending, ct);
        if (!resolved)
            return TypedResults.Problem(
                detail: "The posting still cannot be booked — the account is missing/inactive or the period is closed.",
                statusCode: 409, title: "PendingPosting.StillUnresolvable");

        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(PendingAccountingPostingResponse.From(pending));
    }
}
