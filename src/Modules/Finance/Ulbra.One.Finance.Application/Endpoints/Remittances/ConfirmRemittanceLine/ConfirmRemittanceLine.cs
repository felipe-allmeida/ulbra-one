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
            .WithName("ConfirmRemittanceLine").WithTags("Finance")
            .WithSummary("Confirms one remittance line as paid.")
            .WithDescription("Manually settles a single pending line - the line-by-line counterpart to auto-confirm. It "
                + "REGISTERS THE PAYMENT against the installment, so it is a financial action, not a status "
                + "flag.").RequirePermission(Permissions.TreasuryWrite)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        RemittanceId id,
        RemittanceLineId lineId,
        UserContext user,
        IRemittanceReturnService service,
        CancellationToken ct,
        // Optional CNAB return data (from the reconciliation preview) — recorded on the paid line.
        [SwaggerSchema("Date the bank actually settled it, from the return file. Recorded on the payment.")]
        [SwaggerExample("2026-09-10")]
        DateOnly? effectiveDate = null,
        [SwaggerSchema("Bank reference for the settlement, from the return file.")]
        string? bankReference = null,
        [SwaggerSchema("Authentication code of the settlement, from the return file.")]
        string? authCode = null)
    {
        var processedById = Guid.TryParse(user.UserId, out var uid) ? uid : SystemActor.Id;

        var result = await service.ConfirmLinePaidAsync(lineId.Value, processedById, ct, effectiveDate, bankReference, authCode);

        if (result.IsFailure)
        {
            return result.Error.Code switch
            {
                ErrorCodes.RemittanceLine.NotFound or ErrorCodes.Remittance.NotFound or ErrorCodes.Payable.NotFound
                    => ErrorResults.NotFound(result.Error.Description, result.Error.Code),
                _ => ErrorResults.InvalidRequest(result.Error.Description, result.Error.Code),
            };
        }

        return TypedResults.Ok();
    }
}
