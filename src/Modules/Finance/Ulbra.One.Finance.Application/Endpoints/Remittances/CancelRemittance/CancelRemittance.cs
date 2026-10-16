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
using Ulbra.One.Finance.Application.Remittances;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Remittances.CancelRemittance;

public sealed class CancelRemittance : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/cancel", Handle)
            .WithName("CancelRemittance").WithTags("Finance")
            .WithSummary("Cancels a remittance.")
            .WithDescription("Discards a remittance that was not executed and RELEASES its installments back to the "
                + "payment queue. Use it when the file was never sent to the bank, or the bank rejected it "
                + "wholesale - cancelling one the bank already processed would free installments that were in "
                + "fact paid.").RequirePermission(Permissions.TreasuryWrite)
            .Produces<RemittanceResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        RemittanceId id,
        IUnitOfWork uow,
        IRemittanceGenerationService service,
        CancellationToken ct)
    {
        var result = await service.CancelAsync(id.Value, ct);

        if (result.IsFailure)
        {
            return result.Error.Code switch
            {
                ErrorCodes.Remittance.NotFound => ErrorResults.NotFound(result.Error.Description, result.Error.Code),
                _ => ErrorResults.InvalidRequest(result.Error.Description, result.Error.Code),
            };
        }

        // Reload and return the updated remittance
        var remittance = await uow.ReadSet<Remittance>()
            .FirstOrDefaultAsync(r => r.Id == id.Value, ct);

        return remittance is null
            ? ErrorResults.NotFound("Remittance not found after cancel.", ErrorCodes.Remittance.NotFound)
            : TypedResults.Ok(RemittanceResponse.From(remittance));
    }
}
