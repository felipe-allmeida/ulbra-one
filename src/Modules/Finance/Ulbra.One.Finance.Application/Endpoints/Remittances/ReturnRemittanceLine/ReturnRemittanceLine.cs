using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Application.Remittances;

namespace Ulbra.One.Finance.Application.Endpoints.Remittances.ReturnRemittanceLine;

/// <summary>
/// <c>POST /remittances/{id}/lines/{lineId}/mark-returned</c> — manually marks a Pending
/// remittance line as returned / rejected, releasing the installment back to Open.
/// </summary>
public sealed class ReturnRemittanceLine : IEndpoint
{
    public sealed record Request(
        [property: SwaggerSchema("Why the bank did not pay this line. Required — it is what explains the "
            + "rejection when the installment shows up in the queue again.")]
        [property: SwaggerExample("Conta do favorecido encerrada.")]
        string Reason);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/lines/{lineId}/mark-returned", Handle)
            .WithName("ReturnRemittanceLine").WithTags("Finance")
            .WithSummary("Marks one remittance line as returned.")
            .WithDescription("Manually rejects a single pending line and releases its installment back to the payment "
                + "queue, so it can be remitted again. Nothing is settled.").RequirePermission(Permissions.TreasuryWrite)
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
                ErrorCodes.RemittanceLine.NotFound or ErrorCodes.Remittance.NotFound
                    => ErrorResults.NotFound(result.Error.Description, result.Error.Code),
                _ => ErrorResults.InvalidRequest(result.Error.Description, result.Error.Code),
            };
        }

        return TypedResults.Ok();
    }
}
