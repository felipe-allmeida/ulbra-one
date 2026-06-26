using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Finance.Application.Remittances;

namespace Ulbra.One.Finance.Application.Endpoints.Remittances.ReturnRemittanceLine;

/// <summary>
/// <c>POST /remittances/{id}/lines/{lineId}/mark-returned</c> — manually marks a Pending
/// remittance line as returned / rejected, releasing the installment back to Open.
/// </summary>
public sealed class ReturnRemittanceLine : IEndpoint
{
    public sealed record Request(string Reason);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/lines/{lineId}/mark-returned", Handle)
            .WithName("ReturnRemittanceLine").WithTags("Finance").RequireAuthorization()
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        RemittanceId id,
        RemittanceLineId lineId,
        Request req,
        IRemittanceReturnService service,
        CancellationToken ct)
    {
        var result = await service.MarkLineReturnedAsync(lineId.Value, req.Reason, ct);

        if (result.IsFailure)
        {
            return result.Error.Code switch
            {
                "RemittanceLine.NotFound" or "Remittance.NotFound"
                    => TypedResults.Problem(detail: result.Error.Description, statusCode: 404, title: result.Error.Code),
                _ => TypedResults.Problem(detail: result.Error.Description, statusCode: 400, title: result.Error.Code),
            };
        }

        return TypedResults.Ok();
    }
}
