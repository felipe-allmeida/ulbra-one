using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.Remittances;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Remittances.CancelRemittance;

public sealed class CancelRemittance : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/cancel", Handle)
            .WithName("CancelRemittance").WithTags("Finance").RequirePermission(Permissions.TreasuryWrite)
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
                "Remittance.NotFound" => TypedResults.Problem(detail: result.Error.Description, statusCode: 404, title: result.Error.Code),
                _ => TypedResults.Problem(detail: result.Error.Description, statusCode: 400, title: result.Error.Code),
            };
        }

        // Reload and return the updated remittance
        var remittance = await uow.ReadSet<Remittance>()
            .FirstOrDefaultAsync(r => r.Id == id.Value, ct);

        return remittance is null
            ? TypedResults.Problem(detail: "Remittance not found after cancel.", statusCode: 404, title: "Remittance.NotFound")
            : TypedResults.Ok(RemittanceResponse.From(remittance));
    }
}
