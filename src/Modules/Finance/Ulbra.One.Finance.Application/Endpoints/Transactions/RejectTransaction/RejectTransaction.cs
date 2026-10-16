using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Transactions.RejectTransaction;

public sealed class RejectTransaction : IEndpoint
{
    public sealed record RejectRequest(
        [property: SwaggerSchema("Why it is being rejected. Optional.")]
        string? Reason);

    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id}/reject", Handle)
            .WithName("RejectTransaction")
            .WithSummary("Rejects a legacy transaction.")
            .WithDescription("LEGACY, superseded by the Payable aggregate.")
            .WithTags("Finance")
            .RequirePermission(Permissions.TreasuryApprove)
            .Produces<TransactionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        TransactionId id,
        RejectRequest req,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var t = await uow.Set<Transaction>().FirstOrDefaultAsync(x => x.Id == id.Value, ct);
        if (t is null) return TypedResults.NotFound();
        t.Reject(req.Reason);
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(TransactionResponse.From(t));
    }
}
