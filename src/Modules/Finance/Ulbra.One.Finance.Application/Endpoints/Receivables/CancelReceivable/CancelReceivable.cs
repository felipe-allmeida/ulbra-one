using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Results;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Receivables.CancelReceivable;

public sealed class CancelReceivable : IEndpoint
{
    public sealed record CancelReceivableRequest(
        [property: SwaggerSchema("Why the receivable is being cancelled. Optional, and kept on the record.")]
        [property: SwaggerExample("Contrato encerrado antes da prestacao do servico.")]
        string? Reason);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/cancel", Handle).WithName("CancelReceivable").WithTags("Finance")
            .WithSummary("Cancels a receivable.")
            .WithDescription("Closes a receivable that will not be collected. One with a registered receipt cannot be "
                + "cancelled (409) - reverse the receipt first.")
            .RequirePermission(Permissions.ReceivablesWrite).Produces<ReceivableResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(ReceivableId id, CancelReceivableRequest req,
        UserContext user, IUnitOfWork uow, TimeProvider timeProvider, CancellationToken ct)
    {
        var receivable = await uow.Set<Receivable>().Include(x => x.Installments).FirstOrDefaultAsync(x => x.Id == id.Value, ct);
        if (receivable is null)
            return Result.Failure<ReceivableResponse>(Error.NotFound(ErrorCodes.Receivable.NotFound, "Receivable not found.")).ToOk();
        try
        {
            receivable.Cancel(Guid.Parse(user.UserId), timeProvider.GetUtcNow(), req.Reason);
        }
        catch (InvalidOperationException ex)
        {
            return ErrorResults.InvalidState(ex.Message, ErrorCodes.Receivable.InvalidState);
        }
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(ReceivableResponse.From(receivable, ReceivableClock.Today(timeProvider)));
    }
}
