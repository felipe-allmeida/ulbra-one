using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Domain;
using Ulbra.One.Finance.Application.Remittances;

namespace Ulbra.One.Finance.Application.Endpoints.Remittances.ConfirmRemittanceLine;

/// <summary>
/// <c>POST /remittances/{id}/lines/{lineId}/confirm-paid</c> — manually confirms a Pending
/// remittance line as paid, registering a Payment via <see cref="IRemittanceReturnService"/>.
/// </summary>
public sealed class ConfirmRemittanceLine : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/lines/{lineId}/confirm-paid", Handle)
            .WithName("ConfirmRemittanceLine").WithTags("Finance").RequireAuthorization()
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        RemittanceId id,
        RemittanceLineId lineId,
        UserContext user,
        IRemittanceReturnService service,
        CancellationToken ct)
    {
        var processedById = Guid.TryParse(user.UserId, out var uid) ? uid : SystemActor.Id;

        var result = await service.ConfirmLinePaidAsync(lineId.Value, processedById, ct);

        if (result.IsFailure)
        {
            return result.Error.Code switch
            {
                "RemittanceLine.NotFound" or "Remittance.NotFound" or "Payable.NotFound"
                    => TypedResults.Problem(detail: result.Error.Description, statusCode: 404, title: result.Error.Code),
                _ => TypedResults.Problem(detail: result.Error.Description, statusCode: 400, title: result.Error.Code),
            };
        }

        return TypedResults.Ok();
    }
}
